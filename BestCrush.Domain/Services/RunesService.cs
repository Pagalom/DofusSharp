using BestCrush.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace BestCrush.Domain.Services;

public class RunesService(
    IDbContextFactory<BestCrushDbContext>
        dbContextFactory)
{
    public async Task<IReadOnlyCollection<Rune>>
        GetLocalRunesAsync(
            CancellationToken cancellationToken = default)
    {
        await using BestCrushDbContext context =
            await dbContextFactory
                .CreateDbContextAsync(
                    cancellationToken);

        return await context.Runes
            .AsNoTracking()
            .OrderBy(rune => rune.Name)
            .ToListAsync(
                cancellationToken
            );
    }

    public async Task<IReadOnlyCollection<Rune>>
        GetRunesAsync(
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
    {
        // Conservé pour les appelants historiques.
        // forceRefresh n'a plus de sens : la BDD locale est
        // désormais l'unique source du catalogue des runes.
        _ = forceRefresh;

        return await GetLocalRunesAsync(
            cancellationToken
        );
    }

    public async Task<
        IReadOnlyDictionary<
            Characteristic,
            Rune>>
        GetRunesByCharacteristicAsync(
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
    {
        _ = forceRefresh;

        IReadOnlyCollection<Rune> runes =
            await GetLocalRunesAsync(
                cancellationToken
            );

        Dictionary<
            Characteristic,
            Rune> result = [];

        foreach (
            IGrouping<Characteristic, Rune> group
            in runes.GroupBy(
                rune => rune.Characteristic
            ))
        {
            Rune? basicRune =
                group
                    .Where(rune =>
                        !IsPowerVariant(
                            rune.Name
                        ))
                    .OrderBy(rune =>
                        rune.DofusDbId)
                    .FirstOrDefault()
                ?? group
                    .OrderBy(rune =>
                        rune.DofusDbId)
                    .FirstOrDefault();

            if (basicRune is null)
            {
                continue;
            }

            result[group.Key] =
                basicRune;
        }

        return result;
    }

    private static bool IsPowerVariant(
        string runeName)
    {
        return
            runeName.StartsWith(
                "Rune Pa ",
                StringComparison.OrdinalIgnoreCase
            ) ||
            runeName.StartsWith(
                "Rune Ra ",
                StringComparison.OrdinalIgnoreCase
            );
    }
}
