using Microsoft.EntityFrameworkCore;

namespace BestCrush.Domain.Services;

public sealed partial class HistoryService
{
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
