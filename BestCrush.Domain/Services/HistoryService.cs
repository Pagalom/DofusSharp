using BestCrush.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace BestCrush.Domain.Services;

public enum HistorySortColumn
{
    Date,
    Name,
    Level,
    Type,
    Quantity,
    Value,
    Source,
    DataKind,
    Rune
}

public enum HistorySortDirection
{
    Ascending,
    Descending
}

public enum ItemHistoryDataKind
{
    Price,
    Coefficient
}

public sealed record HistoryPage<T>(
    IReadOnlyList<T> Rows,
    bool HasMore
);

public sealed class MarketHistoryRow
{
    public required Guid ObservationId { get; init; }
    public required long DofusDbId { get; init; }
    public long? DofusDbIconId { get; init; }
    public required string Name { get; init; }
    public required int Level { get; init; }
    public required int Quantity { get; init; }
    public required long Price { get; init; }
    public required MarketPriceSource Source { get; init; }
    public required bool IsCleared { get; init; }
    public required DateTime ObservedAtUtc { get; init; }
}

public sealed class ItemHistoryRow
{
    public required Guid ObservationId { get; init; }
    public required long DofusDbId { get; init; }
    public long? DofusDbIconId { get; init; }
    public required string Name { get; init; }
    public required int Level { get; init; }
    public required EquipmentType Type { get; init; }
    public required ItemHistoryDataKind DataKind { get; init; }
    public required double Value { get; init; }
    public MarketPriceSource? MarketSource { get; init; }
    public CoefficientSource? CoefficientSource { get; init; }
    public required bool IsCleared { get; init; }
    public required DateTime ObservedAtUtc { get; init; }
    public required int SourceSortOrder { get; init; }
    public required int DataKindSortOrder { get; init; }
}

public sealed record CrushHistoryRuneLotWriteModel(
    long Count,
    int LotQuantity,
    long LotPrice,
    bool IsEstimated,
    MarketPriceSource? PriceSource,
    DateTime? PriceObservedAtUtc
);

public sealed record CrushHistoryRuneWriteModel(
    long DofusDbId,
    string RuneName,
    int Quantity,
    double? Value,
    IReadOnlyList<CrushHistoryRuneLotWriteModel> Lots
);

public sealed record CrushHistoryEquipmentWriteModel(
    long DofusDbId,
    string EquipmentName,
    double CoefficientPercent,
    CoefficientSource CoefficientSource,
    int RowY
);

public sealed record CrushHistorySessionWriteModel(
    string ServerName,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    double? TotalValue,
    double DiscountPercent,
    double? DiscountedTotalValue,
    IReadOnlyList<CrushHistoryEquipmentWriteModel> Equipments,
    IReadOnlyList<CrushHistoryRuneWriteModel> Runes
);

public sealed class HistoryService(
    BestCrushDbContext context)
{
    public async Task<Guid> SaveCrushSessionAsync(
        CrushHistorySessionWriteModel sessionData,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                sessionData.ServerName))
        {
            throw new ArgumentException(
                "Le serveur de l'historique ne peut pas être vide.",
                nameof(sessionData)
            );
        }

        if (sessionData.Runes.Count == 0)
        {
            throw new ArgumentException(
                "Une session de concassage historique doit contenir au moins une rune.",
                nameof(sessionData)
            );
        }

        long[] equipmentIds =
            sessionData
                .Equipments
                .Select(equipment =>
                    equipment.DofusDbId)
                .Distinct()
                .ToArray();

        long[] runeIds =
            sessionData
                .Runes
                .Select(rune =>
                    rune.DofusDbId)
                .Distinct()
                .ToArray();

        Dictionary<long, long?>
            equipmentIconIds =
                await context
                    .Equipments
                    .AsNoTracking()
                    .Where(equipment =>
                        equipmentIds.Contains(
                            equipment.DofusDbId))
                    .ToDictionaryAsync(
                        equipment =>
                            equipment.DofusDbId,
                        equipment =>
                            equipment.DofusDbIconId,
                        cancellationToken
                    );

        Dictionary<long, long?>
            runeIconIds =
                await context
                    .Runes
                    .AsNoTracking()
                    .Where(rune =>
                        runeIds.Contains(
                            rune.DofusDbId))
                    .ToDictionaryAsync(
                        rune =>
                            rune.DofusDbId,
                        rune =>
                            rune.DofusDbIconId,
                        cancellationToken
                    );

        CrushHistorySession session =
            new(
                sessionData.ServerName,
                sessionData.StartedAtUtc,
                sessionData.CompletedAtUtc,
                sessionData.TotalValue,
                sessionData.DiscountPercent,
                sessionData.DiscountedTotalValue
            );

        foreach (
            CrushHistoryEquipmentWriteModel equipment
            in sessionData.Equipments)
        {
            session.Equipments.Add(
                new CrushHistoryEquipment(
                    session,
                    equipment.DofusDbId,
                    equipmentIconIds
                        .GetValueOrDefault(
                            equipment.DofusDbId),
                    equipment.EquipmentName,
                    equipment.CoefficientPercent,
                    equipment.CoefficientSource,
                    equipment.RowY
                )
            );
        }

        foreach (
            CrushHistoryRuneWriteModel runeData
            in sessionData.Runes)
        {
            CrushHistoryRune rune =
                new(
                    session,
                    runeData.DofusDbId,
                    runeIconIds
                        .GetValueOrDefault(
                            runeData.DofusDbId),
                    runeData.RuneName,
                    runeData.Quantity,
                    runeData.Value
                );

            foreach (
                CrushHistoryRuneLotWriteModel lot
                in runeData.Lots)
            {
                rune.Lots.Add(
                    new CrushHistoryRuneLot(
                        rune,
                        lot.Count,
                        lot.LotQuantity,
                        lot.LotPrice,
                        lot.IsEstimated,
                        lot.PriceSource,
                        lot.PriceObservedAtUtc
                    )
                );
            }

            session.Runes.Add(
                rune
            );
        }

        context.CrushHistorySessions.Add(
            session
        );

        await context.SaveChangesAsync(
            cancellationToken
        );

        return session.Id;
    }

    public Task<HistoryPage<MarketHistoryRow>>
        GetMarketHistoryAsync(
            MarketObjectType objectType,
            string serverName,
            string? searchText,
            int? levelMin,
            int? levelMax,
            HistorySortColumn sortColumn,
            HistorySortDirection sortDirection,
            int take,
            CancellationToken cancellationToken = default)
    {
        return objectType switch
        {
            MarketObjectType.Resource =>
                GetResourceHistoryAsync(
                    serverName,
                    searchText,
                    levelMin,
                    levelMax,
                    sortColumn,
                    sortDirection,
                    take,
                    cancellationToken
                ),

            MarketObjectType.Rune =>
                GetRuneHistoryAsync(
                    serverName,
                    searchText,
                    levelMin,
                    levelMax,
                    sortColumn,
                    sortDirection,
                    take,
                    cancellationToken
                ),

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(objectType),
                    objectType,
                    "Cet historique de marché est réservé aux ressources et aux runes."
                )
        };
    }

    private async Task<HistoryPage<MarketHistoryRow>>
        GetResourceHistoryAsync(
            string serverName,
            string? searchText,
            int? levelMin,
            int? levelMax,
            HistorySortColumn sortColumn,
            HistorySortDirection sortDirection,
            int take,
            CancellationToken cancellationToken)
    {
        IQueryable<MarketHistoryRow> query =
            from observation in context
                .MarketPriceObservations
                .AsNoTracking()
            join resource in context
                .Resources
                .AsNoTracking()
                on observation.DofusDbId
                equals resource.DofusDbId
            where
                observation.ObjectType ==
                    MarketObjectType.Resource &&
                observation.ServerName ==
                    serverName
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

        query = ApplyMarketSort(
            query,
            sortColumn,
            sortDirection
        );

        return await ToPageAsync(
            query,
            take,
            cancellationToken
        );
    }

    private async Task<HistoryPage<MarketHistoryRow>>
        GetRuneHistoryAsync(
            string serverName,
            string? searchText,
            int? levelMin,
            int? levelMax,
            HistorySortColumn sortColumn,
            HistorySortDirection sortDirection,
            int take,
            CancellationToken cancellationToken)
    {
        IQueryable<MarketHistoryRow> query =
            from observation in context
                .MarketPriceObservations
                .AsNoTracking()
            join rune in context
                .Runes
                .AsNoTracking()
                on observation.DofusDbId
                equals rune.DofusDbId
            where
                observation.ObjectType ==
                    MarketObjectType.Rune &&
                observation.ServerName ==
                    serverName
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

        query = ApplyMarketSort(
            query,
            sortColumn,
            sortDirection
        );

        return await ToPageAsync(
            query,
            take,
            cancellationToken
        );
    }

    public async Task<HistoryPage<ItemHistoryRow>>
        GetItemHistoryAsync(
            string serverName,
            string? searchText,
            int? levelMin,
            int? levelMax,
            EquipmentType? equipmentType,
            long? obtainableRuneDofusDbId,
            HistorySortColumn sortColumn,
            HistorySortDirection sortDirection,
            int take,
            CancellationToken cancellationToken = default)
    {
        IQueryable<Equipment> equipments =
            context
                .Equipments
                .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(
                searchText))
        {
            string pattern =
                $"%{searchText.Trim()}%";

            equipments =
                equipments.Where(equipment =>
                    EF.Functions.Like(
                        equipment.Name,
                        pattern
                    ));
        }

        if (levelMin is int minimum)
        {
            equipments =
                equipments.Where(equipment =>
                    equipment.Level >= minimum);
        }

        if (levelMax is int maximum)
        {
            equipments =
                equipments.Where(equipment =>
                    equipment.Level <= maximum);
        }

        if (equipmentType is EquipmentType type)
        {
            equipments =
                equipments.Where(equipment =>
                    equipment.Type == type);
        }

        if (obtainableRuneDofusDbId is long runeId)
        {
            Characteristic? characteristic =
                await context
                    .Runes
                    .AsNoTracking()
                    .Where(rune =>
                        rune.DofusDbId == runeId)
                    .Select(rune =>
                        (Characteristic?)rune.Characteristic)
                    .SingleOrDefaultAsync(
                        cancellationToken
                    );

            if (characteristic is Characteristic value)
            {
                equipments =
                    equipments.Where(equipment =>
                        equipment.Characteristics.Any(
                            line =>
                                line.Characteristic == value &&
                                (
                                    line.From > 0 ||
                                    line.To > 0
                                )
                        ));
            }
            else
            {
                return new HistoryPage<ItemHistoryRow>(
                    [],
                    false
                );
            }
        }

        IQueryable<ItemHistoryRow> priceQuery =
            from observation in context
                .MarketPriceObservations
                .AsNoTracking()
            join equipment in equipments
                on observation.DofusDbId
                equals equipment.DofusDbId
            where
                observation.ObjectType ==
                    MarketObjectType.Equipment &&
                observation.ServerName ==
                    serverName
            select new ItemHistoryRow
            {
                ObservationId = observation.Id,
                DofusDbId = equipment.DofusDbId,
                DofusDbIconId = equipment.DofusDbIconId,
                Name = equipment.Name,
                Level = equipment.Level,
                Type = equipment.Type,
                DataKind = ItemHistoryDataKind.Price,
                Value = (double)observation.Price,
                MarketSource = observation.Source,
                CoefficientSource = null,
                IsCleared = observation.IsCleared,
                ObservedAtUtc = observation.ObservedAtUtc,
                SourceSortOrder =
                    observation.Source ==
                        MarketPriceSource.Manual
                        ? 0
                        : 1,
                DataKindSortOrder = 0
            };

        IQueryable<ItemHistoryRow> coefficientQuery =
            from observation in context
                .CoefficientObservations
                .AsNoTracking()
            join equipment in equipments
                on observation.DofusDbId
                equals equipment.DofusDbId
            where
                observation.ServerName ==
                    serverName
            select new ItemHistoryRow
            {
                ObservationId = observation.Id,
                DofusDbId = equipment.DofusDbId,
                DofusDbIconId = equipment.DofusDbIconId,
                Name = equipment.Name,
                Level = equipment.Level,
                Type = equipment.Type,
                DataKind = ItemHistoryDataKind.Coefficient,
                Value = observation.CoefficientPercent,
                MarketSource = null,
                CoefficientSource = observation.Source,
                IsCleared = observation.IsCleared,
                ObservedAtUtc = observation.ObservedAtUtc,
                SourceSortOrder =
                    observation.Source ==
                        CoefficientSource.Manual
                        ? 0
                        : observation.Source ==
                            CoefficientSource.InGameAutomatic
                            ? 1
                            : 2,
                DataKindSortOrder = 1
            };

        IQueryable<ItemHistoryRow> query =
            priceQuery.Concat(
                coefficientQuery
            );

        query = ApplyItemSort(
            query,
            sortColumn,
            sortDirection
        );

        return await ToPageAsync(
            query,
            take,
            cancellationToken
        );
    }

    public async Task<HistoryPage<CrushHistorySession>>
        GetCrushHistoryAsync(
            string serverName,
            string? searchText,
            long? obtainedRuneDofusDbId,
            HistorySortColumn sortColumn,
            HistorySortDirection sortDirection,
            int take,
            CancellationToken cancellationToken = default)
    {
        IQueryable<CrushHistorySession> query =
            context
                .CrushHistorySessions
                .AsNoTracking()
                .Where(session =>
                    session.ServerName == serverName);

        if (!string.IsNullOrWhiteSpace(
                searchText))
        {
            string pattern =
                $"%{searchText.Trim()}%";

            query = query.Where(session =>
                session.Equipments.Any(equipment =>
                    EF.Functions.Like(
                        equipment.EquipmentName,
                        pattern
                    )) ||
                session.Runes.Any(rune =>
                    EF.Functions.Like(
                        rune.RuneName,
                        pattern
                    ))
            );
        }

        if (obtainedRuneDofusDbId is long runeId)
        {
            query = query.Where(session =>
                session.Runes.Any(rune =>
                    rune.DofusDbId == runeId));
        }

        query = ApplyCrushSort(
            query,
            sortColumn,
            sortDirection
        );

        List<CrushHistorySession> rows =
            await query
                .Take(
                    Math.Max(1, take) + 1
                )
                .Include(session =>
                    session.Equipments)
                .Include(session =>
                    session.Runes)
                    .ThenInclude(rune =>
                        rune.Lots)
                .AsSplitQuery()
                .ToListAsync(
                    cancellationToken
                );

        bool hasMore =
            rows.Count > take;

        if (hasMore)
        {
            rows.RemoveAt(
                rows.Count - 1
            );
        }

        return new HistoryPage<CrushHistorySession>(
            rows,
            hasMore
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

    private static IQueryable<ItemHistoryRow>
        ApplyItemSort(
            IQueryable<ItemHistoryRow> query,
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

            HistorySortColumn.Type =>
                descending
                    ? query.OrderByDescending(row => row.Type)
                        .ThenBy(row => row.Name)
                    : query.OrderBy(row => row.Type)
                        .ThenBy(row => row.Name),

            HistorySortColumn.DataKind =>
                descending
                    ? query.OrderByDescending(row => row.DataKindSortOrder)
                        .ThenByDescending(row => row.ObservedAtUtc)
                    : query.OrderBy(row => row.DataKindSortOrder)
                        .ThenByDescending(row => row.ObservedAtUtc),

            HistorySortColumn.Value =>
                descending
                    ? query.OrderByDescending(row => row.Value)
                        .ThenByDescending(row => row.ObservedAtUtc)
                    : query.OrderBy(row => row.Value)
                        .ThenByDescending(row => row.ObservedAtUtc),

            HistorySortColumn.Source =>
                descending
                    ? query.OrderByDescending(row => row.SourceSortOrder)
                        .ThenByDescending(row => row.ObservedAtUtc)
                    : query.OrderBy(row => row.SourceSortOrder)
                        .ThenByDescending(row => row.ObservedAtUtc),

            _ =>
                descending
                    ? query.OrderByDescending(row => row.ObservedAtUtc)
                    : query.OrderBy(row => row.ObservedAtUtc)
        };
    }

    private static IQueryable<CrushHistorySession>
        ApplyCrushSort(
            IQueryable<CrushHistorySession> query,
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
                    ? query.OrderByDescending(session =>
                        session.Equipments
                            .OrderBy(equipment =>
                                equipment.EquipmentName)
                            .Select(equipment =>
                                equipment.EquipmentName)
                            .FirstOrDefault())
                    : query.OrderBy(session =>
                        session.Equipments
                            .OrderBy(equipment =>
                                equipment.EquipmentName)
                            .Select(equipment =>
                                equipment.EquipmentName)
                            .FirstOrDefault()),

            HistorySortColumn.Rune =>
                descending
                    ? query.OrderByDescending(session =>
                        session.Runes
                            .OrderBy(rune =>
                                rune.RuneName)
                            .Select(rune =>
                                rune.RuneName)
                            .FirstOrDefault())
                    : query.OrderBy(session =>
                        session.Runes
                            .OrderBy(rune =>
                                rune.RuneName)
                            .Select(rune =>
                                rune.RuneName)
                            .FirstOrDefault()),

            HistorySortColumn.Quantity =>
                descending
                    ? query.OrderByDescending(session =>
                        session.Runes.Sum(rune =>
                            rune.Quantity))
                    : query.OrderBy(session =>
                        session.Runes.Sum(rune =>
                            rune.Quantity)),

            HistorySortColumn.Value =>
                descending
                    ? query.OrderByDescending(session =>
                        session.TotalValue)
                    : query.OrderBy(session =>
                        session.TotalValue),

            _ =>
                descending
                    ? query.OrderByDescending(session =>
                        session.CompletedAtUtc)
                    : query.OrderBy(session =>
                        session.CompletedAtUtc)
        };
    }

    private static async Task<HistoryPage<T>>
        ToPageAsync<T>(
            IQueryable<T> query,
            int take,
            CancellationToken cancellationToken)
    {
        int effectiveTake =
            Math.Max(1, take);

        List<T> rows =
            await query
                .Take(effectiveTake + 1)
                .ToListAsync(
                    cancellationToken
                );

        bool hasMore =
            rows.Count > effectiveTake;

        if (hasMore)
        {
            rows.RemoveAt(
                rows.Count - 1
            );
        }

        return new HistoryPage<T>(
            rows,
            hasMore
        );
    }
}
