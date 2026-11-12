using BestCrush.Domain.Models;
using DofusSharp.DofusDb.ApiClients.Models.Characteristics;
using DofusSharp.DofusDb.ApiClients.Models.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BestCrush.Domain.Services.Upgrades;

public partial class GameDataUpgradeHandler
{
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

            int minimum = effect.DiceNum ?? 0;
            int maximum = effect.DiceSide ?? 0;
            if (minimum == 0 && maximum == 0) minimum = effect.Value ?? 0;
            if (minimum < 0 || maximum < 0 || (minimum == 0 && maximum == 0) || !seen.Add(definition.Stat))
                continue;

            yield return (definition.Stat, minimum * definition.Sign, (maximum == 0 ? minimum : maximum) * definition.Sign);
        }
    }
}
