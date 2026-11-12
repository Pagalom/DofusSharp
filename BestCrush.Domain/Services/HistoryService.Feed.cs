using BestCrush.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace BestCrush.Domain.Services;

public sealed partial class HistoryService
{
    public async Task<HistoryPage<CrushHistoryEntry>> GetCrushingFeedAsync(
        string serverName, string? searchText, long? runeId,
        HistorySortColumn sortColumn, HistorySortDirection sortDirection, int take,
        HistoryQueryOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        // Reuse the exact observations shown in equipment history; no duplicate writes.
        var coefficientItems = await BuildItemHistoryQueryAsync(serverName, searchText, null, null,
            null, runeId, null, options with { ItemDataKind = ItemHistoryDataKind.Coefficient }, cancellationToken);
        var coefficients = coefficientItems.Select(row => new CrushHistoryEntry
        {
            Id = row.ObservationId, ObservedAtUtc = row.ObservedAtUtc, Name = row.Name,
            DofusDbId = row.DofusDbId, Kind = CrushHistoryEntryKind.Coefficient,
            Value = row.Value, Quantity = null, Source = row.CoefficientSource, IsCleared = row.IsCleared
        });
        var sessions = context.CrushHistorySessions.AsNoTracking().Where(s => s.ServerName == serverName);
        if (options.FromUtc is DateTime from) sessions = sessions.Where(s => s.CompletedAtUtc >= from);
        if (options.BeforeUtc is DateTime before) sessions = sessions.Where(s => s.CompletedAtUtc < before);
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            string pattern = $"%{searchText.Trim()}%";
            sessions = sessions.Where(s => s.Equipments.Any(e => EF.Functions.Like(e.EquipmentName, pattern)) ||
                                           s.Runes.Any(r => EF.Functions.Like(r.RuneName, pattern)));
        }
        if (runeId is long selectedRune) sessions = sessions.Where(s => s.Runes.Any(r => r.DofusDbId == selectedRune));
        var operations = sessions.Select(s => new CrushHistoryEntry
        {
            Id = s.Id, ObservedAtUtc = s.CompletedAtUtc,
            Name = s.Equipments.OrderBy(e => e.RowY).Select(e => e.EquipmentName).FirstOrDefault() ?? "Concassage",
            DofusDbId = null, Kind = CrushHistoryEntryKind.Operation,
            Value = s.DiscountedTotalValue ?? s.TotalValue,
            Quantity = s.Equipments.Count(), Source = null, IsCleared = false
        });
        IQueryable<CrushHistoryEntry> query = options.CrushKind switch
        {
            CrushHistoryEntryKind.Coefficient => coefficients,
            CrushHistoryEntryKind.Operation => operations,
            _ => coefficients.Concat(operations)
        };
        bool descending = sortDirection == HistorySortDirection.Descending;
        IOrderedQueryable<CrushHistoryEntry> ordered = sortColumn switch
        {
            HistorySortColumn.Name => descending ? query.OrderByDescending(r => r.Name) : query.OrderBy(r => r.Name),
            HistorySortColumn.Value => descending ? query.OrderByDescending(r => r.Value) : query.OrderBy(r => r.Value),
            HistorySortColumn.Quantity => descending ? query.OrderByDescending(r => r.Quantity) : query.OrderBy(r => r.Quantity),
            HistorySortColumn.DataKind => descending ? query.OrderByDescending(r => r.Kind) : query.OrderBy(r => r.Kind),
            _ => descending ? query.OrderByDescending(r => r.ObservedAtUtc) : query.OrderBy(r => r.ObservedAtUtc)
        };
        var page = await ToPageAsync(ordered.ThenByDescending(r => r.ObservedAtUtc).ThenBy(r => r.Id)
            .Skip(Math.Max(0, options.Skip)), take, cancellationToken);
        // Only hydrate the sessions visible on this page, including their immutable lot snapshots.
        Guid[] ids = page.Rows.Where(r => r.Kind == CrushHistoryEntryKind.Operation).Select(r => r.Id).ToArray();
        if (ids.Length > 0)
        {
            var details = await context.CrushHistorySessions.AsNoTracking().Where(s => ids.Contains(s.Id))
                .Include(s => s.Equipments).Include(s => s.Runes).ThenInclude(r => r.Lots)
                .AsSplitQuery().ToDictionaryAsync(s => s.Id, cancellationToken);
            foreach (var row in page.Rows) row.Session = details.GetValueOrDefault(row.Id);
        }
        return page;
    }

    private static IQueryable<MarketHistoryRow> ApplyObservationOptions(
        IQueryable<MarketHistoryRow> query, HistoryQueryOptions? options)
    {
        if (options is null) return query;
        if (options.FromUtc is DateTime from) query = query.Where(r => r.ObservedAtUtc >= from);
        if (options.BeforeUtc is DateTime before) query = query.Where(r => r.ObservedAtUtc < before);
        if (!options.IncludeCleared) query = query.Where(r => !r.IsCleared);
        if (options.LotQuantity is int lot) query = query.Where(r => r.Quantity == lot);
        query = options.Source switch
        {
            HistoryObservationSource.Manual => query.Where(r => r.Source == MarketPriceSource.Manual),
            HistoryObservationSource.Game => query.Where(r => r.Source == MarketPriceSource.InGameAutomatic),
            HistoryObservationSource.DoFocus => query.Where(r => false),
            _ => query
        };
        return query;
    }

    private static IQueryable<ItemHistoryRow> ApplyObservationOptions(
        IQueryable<ItemHistoryRow> query, HistoryQueryOptions? options)
    {
        if (options is null) return query;
        if (options.FromUtc is DateTime from) query = query.Where(r => r.ObservedAtUtc >= from);
        if (options.BeforeUtc is DateTime before) query = query.Where(r => r.ObservedAtUtc < before);
        if (!options.IncludeCleared) query = query.Where(r => !r.IsCleared);
        if (options.LotQuantity is int lot) query = query.Where(r => r.Quantity == lot);
        if (options.ItemDataKind is ItemHistoryDataKind kind) query = query.Where(r => r.DataKind == kind);
        if (options.EquipmentTypes.Count > 0)
        {
            var types = options.EquipmentTypes.ToArray();
            query = query.Where(r => Enumerable.Contains(types, r.Type));
        }
        query = options.Source switch
        {
            HistoryObservationSource.Manual => query.Where(r => r.MarketSource == MarketPriceSource.Manual || r.CoefficientSource == CoefficientSource.Manual),
            HistoryObservationSource.Game => query.Where(r => r.MarketSource == MarketPriceSource.InGameAutomatic || r.CoefficientSource == CoefficientSource.InGameAutomatic),
            HistoryObservationSource.DoFocus => query.Where(r => r.CoefficientSource == CoefficientSource.DofocusInitial),
            _ => query
        };
        return query;
    }
}
