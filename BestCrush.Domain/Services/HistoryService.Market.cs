using BestCrush.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace BestCrush.Domain.Services;

public sealed partial class HistoryService
{
    public async Task<HistoryPage<MarketHistoryRow>>
        GetMarketHistoryAsync(
            MarketObjectType objectType,
            string serverName,
            string? searchText,
            int? levelMin,
            int? levelMax,
            HistorySortColumn sortColumn,
            HistorySortDirection sortDirection,
            int take,
            CancellationToken cancellationToken = default,
            long? itemId = null,
            HistoryQueryOptions? options = null)
    {
        var observations = context.MarketPriceObservations.AsNoTracking()
            .Where(quote => quote.ObjectType == objectType && quote.ServerName == serverName);
        if (itemId is long id)
            observations = observations.Where(quote => quote.DofusDbId == id);
        if (objectType is MarketObjectType.Resource or MarketObjectType.Rune &&
            options?.Source is not (HistoryObservationSource.Manual or HistoryObservationSource.DoFocus))
        {
            var rejected = await MarketPriceReliability.GetRejectedHistoryIdsAsync(
                observations, options?.FromUtc, options?.BeforeUtc, cancellationToken);
            observations = observations.Where(quote => !rejected.Contains(quote.Id));
        }

        return await (objectType switch
        {
            MarketObjectType.Resource =>
                GetResourceHistoryAsync(
                    observations,
                    searchText,
                    levelMin,
                    levelMax,
                    sortColumn,
                    sortDirection,
                    take,
                    cancellationToken,
                    options
                ),

            MarketObjectType.Rune =>
                GetRuneHistoryAsync(
                    observations,
                    searchText,
                    levelMin,
                    levelMax,
                    sortColumn,
                    sortDirection,
                    take,
                    cancellationToken,
                    options
                ),

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(objectType),
                    objectType,
                    "Cet historique de marché est réservé aux ressources et aux runes."
                )
        });
    }

    private async Task<HistoryPage<MarketHistoryRow>>
        GetResourceHistoryAsync(
            IQueryable<MarketPriceObservation> observations,
            string? searchText,
            int? levelMin,
            int? levelMax,
            HistorySortColumn sortColumn,
            HistorySortDirection sortDirection,
            int take,
            CancellationToken cancellationToken,
            HistoryQueryOptions? options)
    {
        IQueryable<MarketHistoryRow> query =
            from observation in observations
            join resource in context
                .Resources
                .AsNoTracking()
                on observation.DofusDbId
                equals resource.DofusDbId
            select new MarketHistoryRow
            {
                ObservationId = observation.Id,
                DofusDbId = resource.DofusDbId,
                DofusDbIconId = resource.DofusDbIconId,
                Name = resource.Name,
                Level = resource.Level,
                Quantity = observation.Quantity,
                Price = observation.Price,
                Source = observation.Source,
                IsCleared = observation.IsCleared,
                ObservedAtUtc = observation.ObservedAtUtc
            };

        query = ApplyMarketFilters(
            query,
            searchText,
            levelMin,
            levelMax
        );

        query = ApplyObservationOptions(query, options);
        query = ApplyMarketSort(
            query,
            sortColumn,
            sortDirection
        );
        query = ((IOrderedQueryable<MarketHistoryRow>)query).ThenBy(row => row.ObservationId);

        return await ToPageAsync(
            query.Skip(Math.Max(0, options?.Skip ?? 0)),
            take,
            cancellationToken
        );
    }

    private async Task<HistoryPage<MarketHistoryRow>>
        GetRuneHistoryAsync(
            IQueryable<MarketPriceObservation> observations,
            string? searchText,
            int? levelMin,
            int? levelMax,
            HistorySortColumn sortColumn,
            HistorySortDirection sortDirection,
            int take,
            CancellationToken cancellationToken,
            HistoryQueryOptions? options)
    {
        IQueryable<MarketHistoryRow> query =
            from observation in observations
            join rune in context
                .Runes
                .AsNoTracking()
                on observation.DofusDbId
                equals rune.DofusDbId
            select new MarketHistoryRow
            {
                ObservationId = observation.Id,
                DofusDbId = rune.DofusDbId,
                DofusDbIconId = rune.DofusDbIconId,
                Name = rune.Name,
                Level = rune.Level,
                Quantity = observation.Quantity,
                Price = observation.Price,
                Source = observation.Source,
                IsCleared = observation.IsCleared,
                ObservedAtUtc = observation.ObservedAtUtc
            };

        query = ApplyMarketFilters(
            query,
            searchText,
            levelMin,
            levelMax
        );

        query = ApplyObservationOptions(query, options);
        query = ApplyMarketSort(
            query,
            sortColumn,
            sortDirection
        );
        query = ((IOrderedQueryable<MarketHistoryRow>)query).ThenBy(row => row.ObservationId);

        return await ToPageAsync(
            query.Skip(Math.Max(0, options?.Skip ?? 0)),
            take,
            cancellationToken
        );
    }

    private static IQueryable<MarketHistoryRow>
        ApplyMarketFilters(
            IQueryable<MarketHistoryRow> query,
            string? searchText,
            int? levelMin,
            int? levelMax)
    {
        if (!string.IsNullOrWhiteSpace(
                searchText))
        {
            string pattern =
                $"%{searchText.Trim()}%";

            query = query.Where(row =>
                EF.Functions.Like(
                    row.Name,
                    pattern
                ));
        }

        if (levelMin is int minimum)
        {
            query = query.Where(row =>
                row.Level >= minimum);
        }

        if (levelMax is int maximum)
        {
            query = query.Where(row =>
                row.Level <= maximum);
        }

        return query;
    }

    private static IQueryable<MarketHistoryRow>
        ApplyMarketSort(
            IQueryable<MarketHistoryRow> query,
            HistorySortColumn column,
            HistorySortDirection direction)
    {
        bool descending =
            direction ==
            HistorySortDirection.Descending;

        return column switch
        {
            HistorySortColumn.Name =>
                descending
                    ? query.OrderByDescending(row => row.Name)
                        .ThenByDescending(row => row.ObservedAtUtc)
                    : query.OrderBy(row => row.Name)
                        .ThenByDescending(row => row.ObservedAtUtc),

            HistorySortColumn.Level =>
                descending
                    ? query.OrderByDescending(row => row.Level)
                        .ThenBy(row => row.Name)
                    : query.OrderBy(row => row.Level)
                        .ThenBy(row => row.Name),

            HistorySortColumn.Quantity =>
                descending
                    ? query.OrderByDescending(row => row.Quantity)
                        .ThenByDescending(row => row.ObservedAtUtc)
                    : query.OrderBy(row => row.Quantity)
                        .ThenByDescending(row => row.ObservedAtUtc),

            HistorySortColumn.Value =>
                descending
                    ? query.OrderByDescending(row => row.Price)
                        .ThenByDescending(row => row.ObservedAtUtc)
                    : query.OrderBy(row => row.Price)
                        .ThenByDescending(row => row.ObservedAtUtc),

            HistorySortColumn.Source =>
                descending
                    ? query.OrderByDescending(row => row.Source)
                        .ThenByDescending(row => row.ObservedAtUtc)
                    : query.OrderBy(row => row.Source)
                        .ThenByDescending(row => row.ObservedAtUtc),

            _ =>
                descending
                    ? query.OrderByDescending(row => row.ObservedAtUtc)
                    : query.OrderBy(row => row.ObservedAtUtc)
        };
    }

}
