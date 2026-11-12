using BestCrush.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace BestCrush.Domain.Services;

public sealed partial class HistoryService
{
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
            CancellationToken cancellationToken = default,
            long? itemId = null,
            HistoryQueryOptions? options = null)
    {
        var query = await BuildItemHistoryQueryAsync(serverName, searchText, levelMin, levelMax,
            equipmentType, obtainableRuneDofusDbId, itemId, options, cancellationToken);
        query = ((IOrderedQueryable<ItemHistoryRow>)ApplyItemSort(query, sortColumn, sortDirection))
            .ThenBy(row => row.ObservationId);
        return await ToPageAsync(query.Skip(Math.Max(0, options?.Skip ?? 0)), take, cancellationToken);
    }

    private async Task<IQueryable<ItemHistoryRow>> BuildItemHistoryQueryAsync(
        string serverName, string? searchText, int? levelMin, int? levelMax,
        EquipmentType? equipmentType, long? obtainableRuneDofusDbId, long? itemId,
        HistoryQueryOptions? options, CancellationToken cancellationToken)
    {
        IQueryable<Equipment> equipments =
            context
                .Equipments
                .AsNoTracking();

        if (itemId is long selectedItemId)
            equipments = equipments.Where(item => item.DofusDbId == selectedItemId);

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
                equipments = equipments.Where(equipment => false);
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
                Quantity = observation.Quantity,
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
                Quantity = null,
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

        return ApplyObservationOptions(query, options);
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

}
