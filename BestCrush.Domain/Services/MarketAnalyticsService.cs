using BestCrush.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace BestCrush.Domain.Services;

/// <summary>Loads a lightweight catalog and one shared observation batch for an entire comparison.</summary>
public sealed partial class MarketAnalyticsService(BestCrushDbContext context)
{
    public async Task<IReadOnlyList<MarketAnalyticsCatalogItem>> GetCatalogAsync(CancellationToken token = default)
    {
        var equipment = await context.Equipments.AsNoTracking().Select(e => new { e.DofusDbId,e.Name,e.Level,e.Type }).ToArrayAsync(token);
        var traits = await context.Set<ItemCharacteristicLine>().AsNoTracking()
            .Where(c => c.From > 0 || c.To > 0).Select(c => new { c.Equipment.DofusDbId,c.Characteristic }).Distinct().ToArrayAsync(token);
        var byId = traits.GroupBy(t => t.DofusDbId).ToDictionary(g => g.Key,g => (IReadOnlyList<Characteristic>)g.Select(t => t.Characteristic).ToArray());
        var resources = await context.Resources.AsNoTracking().Select(r => new { r.DofusDbId,r.Name,r.Level }).ToArrayAsync(token);
        var runes = await context.Runes.AsNoTracking().Select(r => new { r.DofusDbId,r.Name,r.Level,r.Characteristic }).ToArrayAsync(token);
        return equipment.Select(e => new MarketAnalyticsCatalogItem(new(MarketObjectType.Equipment,e.DofusDbId),e.Name,e.Level,e.Type,byId.GetValueOrDefault(e.DofusDbId) ?? []))
            .Concat(resources.Select(r => new MarketAnalyticsCatalogItem(new(MarketObjectType.Resource,r.DofusDbId),r.Name,r.Level,null,[])))
            .Concat(runes.Select(r => new MarketAnalyticsCatalogItem(new(MarketObjectType.Rune,r.DofusDbId),r.Name,r.Level,null,[r.Characteristic])))
            .OrderBy(i => i.Name).ToArray();
    }

    public async Task<MarketAnalyticsComparison> BuildComparisonAsync(MarketAnalyticsRequest request,
        IReadOnlyList<MarketAnalyticsCatalogItem> catalog, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.ServerName)) throw new ArgumentException("Choisissez un serveur.");
        if (request.Series.Select(s => s.Id).Distinct().Count() != request.Series.Count) throw new ArgumentException("Les courbes doivent avoir des identifiants distincts.");
        if (request.MaxQuoteAgeHours is < 1 or > 8760 || request.MaxPoints is < 2 or > 512) throw new ArgumentException("Paramètres de période invalides.");
        var selections = request.Series.Select(d => ResolveMembers(d,catalog)).ToArray();
        var keys = selections.SelectMany(m => m).Select(m => m.Key).Distinct().ToArray();
        DateTime to = request.ToUtc ?? DateTime.UtcNow;
        DateTime? from = request.FromUtc ?? request.Period switch
        {
            MarketAnalyticsPeriod.Hours24 => to.AddHours(-24),MarketAnalyticsPeriod.Days7 => to.AddDays(-7),
            MarketAnalyticsPeriod.Days30 => to.AddDays(-30),_ => null
        };
        if (from >= to) throw new ArgumentException("Période invalide.");
        long[] equipmentIds = keys.Where(k => k.ObjectType == MarketObjectType.Equipment).Select(k => k.DofusDbId).ToArray();
        long[] resourceIds = keys.Where(k => k.ObjectType == MarketObjectType.Resource).Select(k => k.DofusDbId).ToArray();
        long[] runeIds = keys.Where(k => k.ObjectType == MarketObjectType.Rune).Select(k => k.DofusDbId).ToArray();
        var query = context.MarketPriceObservations.AsNoTracking().Where(o => o.ServerName == request.ServerName && o.Quantity > 0 &&
            ((o.ObjectType == MarketObjectType.Equipment && Enumerable.Contains(equipmentIds,o.DofusDbId)) ||
             (o.ObjectType == MarketObjectType.Resource && Enumerable.Contains(resourceIds,o.DofusDbId)) ||
             (o.ObjectType == MarketObjectType.Rune && Enumerable.Contains(runeIds,o.DofusDbId))));
        // Shared filters are safe to push into SQL; different series still share the same read.
        if (request.Series.Count > 0 && request.Series.Select(d => d.SourceMode).Distinct().Count() == 1 && request.Series[0].SourceMode != MarketAnalyticsSourceMode.All)
        {
            var source = request.Series[0].SourceMode == MarketAnalyticsSourceMode.Manual ? MarketPriceSource.Manual : MarketPriceSource.InGameAutomatic;
            query = query.Where(o => o.Source == source);
        }
        int? commonLot = request.Series.Count > 0 && request.Series.Select(d => d.LotQuantity).Distinct().Count() == 1
            ? request.Series[0].LotQuantity : null;
        var maxAge = TimeSpan.FromHours(request.MaxQuoteAgeHours);
        DateTime? lookback = from.HasValue && from.Value >= DateTime.MinValue + maxAge ? from.Value - maxAge : null;
        var quotes = keys.Length == 0 ? [] : await MarketPriceReliability.IncludeReferenceWindow(query, lookback, to)
            .Where(o => o.Price > 0 || o.IsCleared)
            .OrderBy(o => o.ObservedAtUtc).ThenBy(o => o.Id).ToArrayAsync(token);
        var rejected = MarketPriceReliability.GetRejectedHistoryIds(quotes);
        var selected = quotes.Where(o => o.ObservedAtUtc <= to && (!commonLot.HasValue || o.Quantity == commonLot.Value));
        if (!from.HasValue)
        {
            from = selected.Where(o => !o.IsCleared && !rejected.Contains(o.Id))
                .Select(o => (DateTime?)DateTime.SpecifyKind(o.ObservedAtUtc, DateTimeKind.Utc)).FirstOrDefault();
            if (from >= to) from = to.AddTicks(-1);
        }

        from ??= to.AddDays(-7);
        var observations = selected.Where(o => !lookback.HasValue || o.ObservedAtUtc >= lookback.Value)
            .Select(o =>
            {
                // An excluded quote ends the carry of an older price for that lot.
                bool isRejected = rejected.Contains(o.Id);
                return new MarketAnalyticsObservation(new(o.ObjectType,o.DofusDbId),
                    DateTime.SpecifyKind(o.ObservedAtUtc,DateTimeKind.Utc),
                    isRejected ? 0 : o.Price,o.Quantity,o.Source,o.IsCleared || isRejected);
            }).ToArray();
        return MarketAnalyticsCalculator.Calculate(request with { FromUtc = from,ToUtc = to },selections,observations,token);
    }

    public static IReadOnlyList<MarketAnalyticsMember> ResolveMembers(MarketAnalyticsSeriesDefinition definition,
        IReadOnlyList<MarketAnalyticsCatalogItem> catalog)
    {
        if (definition.LotQuantity is <= 0) throw new ArgumentException("Le lot doit être positif.");
        if (definition.LevelMin > definition.LevelMax) throw new ArgumentException("Les niveaux minimum et maximum sont inversés.");
        if (definition.SelectionMode == MarketAnalyticsSelectionMode.Single)
        {
            if (definition.DofusDbId <= 0) return [];
            var key = new MarketAnalyticsItemKey(definition.ObjectType,definition.DofusDbId);
            return [new(definition.ObjectType,definition.DofusDbId,catalog.FirstOrDefault(c => c.Key == key)?.Name ?? definition.ItemName)];
        }
        if (definition.SelectionMode == MarketAnalyticsSelectionMode.Custom)
        {
            var lookup = catalog.ToDictionary(c => c.Key);
            return definition.Members.DistinctBy(m => m.Key).Where(m => m.DofusDbId > 0)
                .Select(m => m with { Name = lookup.GetValueOrDefault(m.Key)?.Name ?? m.Name }).ToArray();
        }
        IEnumerable<MarketAnalyticsCatalogItem> items = catalog.Where(c => c.Key.ObjectType == definition.ObjectType);
        if (definition.SelectionMode == MarketAnalyticsSelectionMode.Criteria)
        {
            if (definition.Characteristic is Characteristic characteristic) items = items.Where(i => i.Characteristics.Contains(characteristic));
            if (definition.EquipmentTypes.Count > 0) items = items.Where(i => i.EquipmentType is EquipmentType type && definition.EquipmentTypes.Contains(type));
            if (definition.LevelMin is int min) items = items.Where(i => i.Level >= min);
            if (definition.LevelMax is int max) items = items.Where(i => i.Level <= max);
            if (!string.IsNullOrWhiteSpace(definition.SearchText)) items = items.Where(i => i.Name.Contains(definition.SearchText.Trim(),StringComparison.CurrentCultureIgnoreCase));
        }
        return items.Select(i => new MarketAnalyticsMember(i.Key.ObjectType,i.Key.DofusDbId,i.Name)).ToArray();
    }
}
