using BestCrush.Domain.Models;
using DofusSharp.DofusDb.ApiClients;
using DofusSharp.DofusDb.ApiClients.Search;
using DofusSharp.DofusDb.ApiClients.Models.Characteristics;
using DofusSharp.DofusDb.ApiClients.Models.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BestCrush.Domain.Services.Upgrades;

public partial class GameDataUpgradeHandler
{
    /// <summary>Additive repair for catalogues imported before the binary hunting effect was recognized.</summary>
    public async Task RepairNativeHuntingAsync(IDofusDbClientsFactory clients, Version version, CancellationToken token)
    {
        string key = $"native-hunting-v1:{version}";
        if (await dbContext.Upgrades.AnyAsync(u => u.Kind == UpgradeKind.EquipmentEffects && u.NewVersion == key, token)) return;
        List<DofusDbItem> items = [];
        int expected = 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            var client = clients.Items();
            for (int skip = 0; skip < 512; skip += 50)
            {
                var page = await client.SearchAsync(new DofusDbSearchQuery
                {
                    Limit = 50, Skip = skip, Select = ["id", "possibleEffects"],
                    Predicates = [new DofusDbSearchPredicate.Eq("possibleEffects.baseEffectId", "324")]
                }, timeout.Token);
                if (page.Total is <= 0 or > 512 || page.Data.Count == 0)
                    throw new InvalidDataException("Incomplete native hunting equipment catalogue.");
                if (skip > 0 && expected != page.Total) throw new InvalidDataException("Hunting catalogue changed during the repair.");
                expected = page.Total;
                items.AddRange(page.Data);
                if (items.Count >= page.Total) break;
            }
            if (items.Select(item => item.Id).Distinct().Count() != expected)
                throw new InvalidDataException("Incomplete native hunting equipment catalogue.");
        }
        catch (Exception error) when (!token.IsCancellationRequested && error is HttpRequestException or IOException or System.Text.Json.JsonException or OperationCanceledException)
        {
            logger.LogWarning(error, "Native hunting equipment repair deferred; existing data retained.");
            return;
        }
        long[] ids = items.Where(item => item.Id is > 0 && ReadCharacteristicRanges(item, [])
                .Any(line => line.Stat == Characteristic.Hunting)).Select(item => item.Id!.Value).Distinct().ToArray();
        if (ids.Length == 0) { logger.LogWarning("Native hunting catalogue contains no recognized hunting effects."); return; }
        var equipment = await dbContext.Equipments.Include(item => item.Characteristics)
            .Where(item => ids.Contains(item.DofusDbId)).ToArrayAsync(token);
        foreach (var item in equipment)
        {
            var line = item.Characteristics.FirstOrDefault(stat => stat.Characteristic == Characteristic.Hunting);
            if (line is null) item.Characteristics.Add(new(item, Characteristic.Hunting, 1, 1));
            else { line.From = 1; line.To = 1; }
        }
        dbContext.Upgrades.Add(new() { Kind = UpgradeKind.EquipmentEffects, NewVersion = key, UpgradeDate = DateTime.UtcNow });
        await dbContext.SaveChangesAsync(token);
    }

    private async Task<DofusDbItem[]> FetchEquipmentAsync(
        ProgressSync<ProgressMessage>? progress, CancellationToken token)
    {
        long[] equipmentTypes = Enum.GetValues<EquipmentType>().Select(type => type.ToDofusDbItemTypeId()).ToArray();
        return await dofusDbQueryProvider.Items().Where(item => equipmentTypes.Contains(item.TypeId!.Value))
            .ExecuteAsync(progress?.ToMultiSearchProgress("Récupération des équipements"), token)
            .ToArrayAsync(token);
    }

    private async Task RestoreEquipmentCharacteristicsAsync(
        ProgressSync<ProgressMessage>? progress, CancellationToken token)
    {
        var characteristics = await dofusDbQueryProvider.Characteristics()
            .ExecuteAsync(progress?.DeriveSubtask(0, 10).ToMultiSearchProgress("Récupération des caractéristiques"), token)
            .Where(stat => stat.Id.HasValue)
            .ToDictionaryAsync(stat => stat.Id!.Value, cancellationToken: token);
        var items = await FetchEquipmentAsync(progress?.DeriveSubtask(10, 85), token);
        var equipmentById = await dbContext.Equipments.ToDictionaryAsync(equipment => equipment.DofusDbId, token);

        int restored = 0;
        foreach (var item in items)
        {
            token.ThrowIfCancellationRequested();
            if (item.Id is not { } id || !equipmentById.TryGetValue(id, out var equipment)) continue;

            CreateCharacteristicLines(item, equipment, characteristics);
            restored += equipment.Characteristics.Count;
        }

        if (restored == 0)
            throw new InvalidDataException("Le catalogue reçu ne permet pas de rétablir les caractéristiques des équipements.");

        await dbContext.SaveChangesAsync(token);
        logger.LogInformation("Restored {Count} equipment characteristic lines from DofusDB.", restored);
    }

    private static void CreateCharacteristicLines(
        DofusDbItem item, Equipment equipment, Dictionary<long, DofusDbCharacteristic> characteristics)
    {
        foreach (var (stat, from, to) in ReadCharacteristicRanges(item, characteristics))
            equipment.Characteristics.Add(new ItemCharacteristicLine(equipment, stat, from, to));
    }

    private static IEnumerable<(Characteristic Stat, int From, int To)> ReadCharacteristicRanges(
        DofusDbItem item, Dictionary<long, DofusDbCharacteristic> characteristics)
    {
        HashSet<Characteristic> seen = [];
        foreach (var effect in item.Effects ?? [])
        {
            if (effect.EffectId == 795)
            {
                if (seen.Add(Characteristic.Hunting)) yield return (Characteristic.Hunting, 1, 1);
                continue;
            }
            if (effect.Characteristic is not { } id ||
                characteristics.GetValueOrDefault(id)?.Keyword is not { } keyword ||
                CharacteristicExtensions.CharacteristicFromDofusDbKeyword(keyword) is not { } stat ||
                !seen.Add(stat))
                continue;

            int from = effect.From ?? 0;
            yield return (stat, from, effect.To is null or 0 ? from : effect.To.Value);
        }

        foreach (var effect in item.PossibleEffects ?? [])
        {
            if (EquipmentBaseEffects.Resolve(effect.BaseEffectId) is not { } definition)
                continue;

            if (definition.Stat == Characteristic.Hunting)
            {
                if (seen.Add(definition.Stat)) yield return (definition.Stat, 1, 1);
                continue;
            }
            int minimum = effect.DiceNum ?? 0;
            int maximum = effect.DiceSide ?? 0;
            if (minimum == 0 && maximum == 0) minimum = effect.Value ?? 0;
            if (minimum < 0 || maximum < 0 || (minimum == 0 && maximum == 0) || !seen.Add(definition.Stat))
                continue;

            yield return (definition.Stat, minimum * definition.Sign, (maximum == 0 ? minimum : maximum) * definition.Sign);
        }
    }
}
