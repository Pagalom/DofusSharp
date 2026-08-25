using BestCrush.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace BestCrush.Domain.Services;

public class CoefficientService(BestCrushDbContext context)
{
    public async Task<CoefficientObservation> AddObservationAsync(
        long dofusDbId,
        string serverName,
        double coefficientPercent,
        CoefficientSource source,
        CancellationToken cancellationToken = default)
    {
        if (coefficientPercent <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coefficientPercent),
                "Coefficient must be greater than zero."
            );
        }

        CoefficientObservation observation = new()
        {
            DofusDbId = dofusDbId,
            ServerName = serverName,
            CoefficientPercent = coefficientPercent,
            Source = source,
            ObservedAtUtc = DateTime.UtcNow
        };

        context.CoefficientObservations.Add(observation);
        await context.SaveChangesAsync(cancellationToken);

        return observation;
    }

    public Task<CoefficientObservation?> GetLatestObservationAsync(
        long dofusDbId,
        string serverName,
        CancellationToken cancellationToken = default)
    {
        return context.CoefficientObservations
            .AsNoTracking()
            .Where(c =>
                c.DofusDbId == dofusDbId &&
                c.ServerName == serverName)
            .OrderByDescending(c => c.ObservedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<long, CoefficientObservation>>
        GetLatestObservationsForServerAsync(
            string serverName,
            CancellationToken cancellationToken = default)
    {
        List<CoefficientObservation> observations =
            await context.CoefficientObservations
                .AsNoTracking()
                .Where(c => c.ServerName == serverName)
                .OrderByDescending(c => c.ObservedAtUtc)
                .ToListAsync(cancellationToken);

        return observations
            .GroupBy(c => c.DofusDbId)
            .ToDictionary(
                group => group.Key,
                group => group.First()
            );
    }
}