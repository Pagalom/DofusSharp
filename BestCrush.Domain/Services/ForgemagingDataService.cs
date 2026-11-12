using BestCrush.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace BestCrush.Domain.Services;

public sealed record ForgemagingEquipmentChoice(long Id, string Name, int Level);
public sealed record ForgemagingEquipmentData(ForgemagingSession Session, IReadOnlyList<ForgemagingRune> Runes)
{
    public ForgemagingCrushContext? Crushing { get; init; }
}

public sealed class ForgemagingDataService(BestCrushDbContext context, MarketPriceService prices,
    CoefficientService? coefficients = null, IBestCrushSettingsProvider? settings = null)
{
    private static readonly EquipmentType[] ExcludedTypes = [EquipmentType.Pet, EquipmentType.Petsmount, EquipmentType.Mount, EquipmentType.Trophy];

    public async Task<IReadOnlyList<ForgemagingEquipmentChoice>> GetCatalogAsync(CancellationToken token) =>
        await context.Equipments.AsNoTracking().Where(e => !Enumerable.Contains(ExcludedTypes, e.Type)).OrderBy(e => e.Name)
            .Select(e => new ForgemagingEquipmentChoice(e.DofusDbId, e.Name, e.Level)).ToArrayAsync(token);

    public async Task<ForgemagingEquipmentData> LoadAsync(long id, string server, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(server)) throw new ArgumentException("Choisissez un serveur.");
        var item = await context.Equipments.AsNoTracking().Include(e => e.Characteristics)
            .SingleAsync(e => e.DofusDbId == id && !Enumerable.Contains(ExcludedTypes, e.Type), token);
        var runes = await context.Runes.AsNoTracking().OrderBy(r => r.Name).ToArrayAsync(token);
        var observations = await prices.GetLatestObservationsForServerAsync(MarketObjectType.Rune, server, token);
        var unitPrices = observations.Values.Where(p => p.Quantity > 0 && p.Price > 0 && !p.IsCleared)
            .GroupBy(p => p.DofusDbId).ToDictionary(g => g.Key, g => g.Min(p => p.Price / (double)p.Quantity));
        var session = new ForgemagingSession { ItemId = id, ItemName = item.Name, Level = item.Level,
            Lines = item.Characteristics.OrderBy(c => c.Characteristic).Select(c => new ForgemagingLine {
                Characteristic = c.Characteristic, Minimum = Math.Min(c.From, c.To), Maximum = Math.Max(c.From, c.To),
                Target = Math.Max(c.From, c.To) }).ToList() };
        var coefficient = coefficients is null ? null : await coefficients.GetLatestObservationAsync(id, server, token);
        return new(session, runes.Select(r => new ForgemagingRune(r.DofusDbId, r.Name, r.Characteristic,
            RuneGain(r.Name, r.Characteristic), unitPrices.TryGetValue(r.DofusDbId, out var price) ? price : null)).ToArray())
        { Crushing = new(coefficient?.CoefficientPercent, coefficient?.ObservedAtUtc, settings?.CrushValueMultiplier ?? 1) };
    }

    public static int RuneGain(string name, Characteristic characteristic)
    {
        int multiple = name.StartsWith("Rune Ra ", StringComparison.OrdinalIgnoreCase) ? 10 :
            name.StartsWith("Rune Pa ", StringComparison.OrdinalIgnoreCase) ? 3 : 1;
        return checked((int)Math.Round(characteristic.GetBasicRuneWeight() / characteristic.GetWeight() * multiple));
    }
}
