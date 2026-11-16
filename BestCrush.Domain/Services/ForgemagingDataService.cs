using BestCrush.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace BestCrush.Domain.Services;

public sealed record ForgemagingEquipmentChoice(long Id, string Name, int Level)
{
    public long? IconId { get; init; }
}
public sealed record ForgemagingRuneMarket(IReadOnlyList<ForgemagingRune> Runes,
    IReadOnlyDictionary<(long DofusDbId, int Quantity), MarketPriceObservation> ResalePrices);
public sealed record ForgemagingEquipmentData(ForgemagingSession Session, IReadOnlyList<ForgemagingRune> Runes)
{
    public ForgemagingCrushContext? Crushing { get; init; }
    public long? EquipmentIconId { get; init; }
}

public sealed record CrushingItemMarket(ForgemagingRuneMarket Runes, ForgemagingCrushContext Context,
    MarketPriceObservation? Purchase, double? SuggestedSale);

public sealed class ForgemagingDataService(BestCrushDbContext context, MarketPriceService prices,
    CoefficientService? coefficients = null, IBestCrushSettingsProvider? settings = null)
{
    private static readonly EquipmentType[] ExcludedTypes = [EquipmentType.Pet, EquipmentType.Petsmount, EquipmentType.Mount, EquipmentType.Trophy];

    public Task<Dictionary<long, long>> GetRuneIconIdsAsync(long[] runeIds, CancellationToken token) =>
        context.Runes.AsNoTracking().Where(rune => runeIds.Contains(rune.DofusDbId) && rune.DofusDbIconId > 0)
            .ToDictionaryAsync(rune => rune.DofusDbId, rune => rune.DofusDbIconId!.Value, token);

    public async Task<Dictionary<long, CraftCostResult>> GetEquipmentCraftCostsAsync(string server, long[] ids, CancellationToken token)
    {
        var equipment = await context.Equipments.AsNoTracking()
            .Where(item => ids.Contains(item.DofusDbId) && (item.Recipe.Any() || item.EquipmentRecipe.Any()))
            .Include(item => item.Recipe).ThenInclude(entry => entry.Resource)
            .Include(item => item.EquipmentRecipe).ThenInclude(entry => entry.IngredientEquipment)
            .AsSplitQuery().ToArrayAsync(token);
        if (equipment.Length == 0) return [];

        var resources = await prices.GetLatestObservationsForServerAsync(MarketObjectType.Resource, server, token, excludeUnavailableLots: true);
        var equipmentPrices = await prices.GetLatestObservationsForServerAsync(MarketObjectType.Equipment, server, token, excludeUnavailableLots: true);
        var craft = new CraftCostService(prices);
        return equipment.ToDictionary(item => item.DofusDbId, item => craft.Calculate(item, resources, equipmentPrices));
    }

    public Task<Equipment[]> GetOwnedEquipmentAsync(long[] ids, CancellationToken token, bool forCrushing = false) =>
        context.Equipments.AsNoTracking().Include(e => e.Characteristics)
            .Where(e => ids.Contains(e.DofusDbId) && (forCrushing || !Enumerable.Contains(ExcludedTypes, e.Type))).ToArrayAsync(token);

    public async Task<IReadOnlyList<ForgemagingEquipmentChoice>> GetCatalogAsync(CancellationToken token) =>
        await context.Equipments.AsNoTracking().Where(e => !Enumerable.Contains(ExcludedTypes, e.Type)).OrderBy(e => e.Name)
            .Select(e => new ForgemagingEquipmentChoice(e.DofusDbId, e.Name, e.Level) { IconId = e.DofusDbIconId }).ToArrayAsync(token);

    public async Task<ForgemagingEquipmentData> LoadAsync(long id, string server, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(server)) throw new ArgumentException("Choisissez un serveur.");
        var item = await context.Equipments.AsNoTracking().Include(e => e.Characteristics)
            .SingleAsync(e => e.DofusDbId == id && !Enumerable.Contains(ExcludedTypes, e.Type), token);
        var runes = await GetRuneMarketAsync(server, token);
        var session = new ForgemagingSession { ItemId = id, ItemName = item.Name, Level = item.Level,
            Lines = item.Characteristics.OrderBy(c => c.Characteristic).Select(c => new ForgemagingLine {
                Characteristic = c.Characteristic, Minimum = Math.Min(c.From, c.To), Maximum = Math.Max(c.From, c.To),
                Target = Math.Max(c.From, c.To) }).ToList() };
        var coefficient = coefficients is null ? null : await coefficients.GetLatestObservationAsync(id, server, token);
        var resalePrices = runes.ResalePrices.Values.Any(quote => quote.Source != MarketPriceSource.InGameAutomatic)
            ? await prices.GetLatestObservationsForServerAsync(MarketObjectType.Rune, server, token,
                excludeUnavailableLots: true, source: MarketPriceSource.InGameAutomatic)
            : runes.ResalePrices;
        return new(session, runes.Runes)
        {
            EquipmentIconId = item.DofusDbIconId,
            Crushing = new(coefficient?.CoefficientPercent, coefficient?.ObservedAtUtc, settings?.CrushValueMultiplier ?? 1)
                { ResalePrices = resalePrices }
        };
    }

    public async Task<ForgemagingRuneMarket> GetRuneMarketAsync(string server, CancellationToken token, bool gameOnly = false)
    {
        var runes = await context.Runes.AsNoTracking().OrderBy(r => r.Name).ToArrayAsync(token);
        var observations = await prices.GetLatestObservationsForServerAsync(MarketObjectType.Rune, server, token,
            excludeUnavailableLots: true, source: gameOnly ? MarketPriceSource.InGameAutomatic : null);
        var quotes = observations.Values.GroupBy(quote => quote.DofusDbId)
            .ToDictionary(group => group.Key, group => MarketPriceService.CheapestUnitQuote(group));
        return new(runes.Select(rune => new ForgemagingRune(rune.DofusDbId, rune.Name, rune.Characteristic,
            RuneGain(rune.Name, rune.Characteristic), quotes.GetValueOrDefault(rune.DofusDbId) is { } quote
                ? (double)quote.Price / quote.Quantity : null) { IconId = rune.DofusDbIconId }).ToArray(), observations);
    }

    public async Task<CrushingItemMarket> GetCrushingItemMarketAsync(long id, string server, CancellationToken token)
    {
        var runes = await GetRuneMarketAsync(server, token, gameOnly: true);
        var coefficient = coefficients is null ? null : await coefficients.GetLatestObservationAsync(id, server, token);
        var quotes = await prices.GetLatestObservationsForServerAsync(MarketObjectType.Equipment, server, token, excludeUnavailableLots: true);
        var purchase = quotes.GetValueOrDefault((id, 1));
        var crush = new ForgemagingCrushContext(coefficient?.CoefficientPercent, coefficient?.ObservedAtUtc,
            settings?.CrushValueMultiplier ?? 1) { ResalePrices = runes.ResalePrices };
        return new(runes, crush, purchase, purchase is { Price: > 0 } ? MarketPriceService.ResaleLotPrice(purchase.Price) : null);
    }

    public async Task<(DateTime At, double Value)[]> GetCrushingHistoryAsync(long id, string server, bool coefficient,
        DateTime from, CancellationToken token)
    {
        if (coefficient)
        {
            var history = await context.CoefficientObservations.AsNoTracking()
                .Where(row => row.DofusDbId == id && row.ServerName == server && row.ObservedAtUtc >= from &&
                    !row.IsCleared && row.CoefficientPercent > 0)
                .OrderBy(row => row.ObservedAtUtc).ThenBy(row => row.Id)
                .Select(row => new { row.ObservedAtUtc, row.CoefficientPercent }).ToArrayAsync(token);
            return history.Select(row => (row.ObservedAtUtc, row.CoefficientPercent)).ToArray();
        }

        var quotes = await prices.GetHistoryAsync(MarketObjectType.Equipment, id, server, from,
            source: MarketPriceSource.InGameAutomatic, quantity: 1, cancellationToken: token);
        return quotes.Where(row => !row.IsCleared && row.Price > 0).OrderBy(row => row.ObservedAtUtc)
            .Select(row => (row.ObservedAtUtc, (double)row.Price)).ToArray();
    }

    public static int RuneGain(string name, Characteristic characteristic)
    {
        int multiple = name.StartsWith("Rune Ra ", StringComparison.OrdinalIgnoreCase) ? 10 :
            name.StartsWith("Rune Pa ", StringComparison.OrdinalIgnoreCase) ? 3 : 1;
        return checked((int)Math.Round(characteristic.GetBasicRuneWeight() / characteristic.GetWeight() * multiple));
    }
}
