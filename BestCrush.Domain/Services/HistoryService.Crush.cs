using BestCrush.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace BestCrush.Domain.Services;

public sealed partial class HistoryService
{
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
}
