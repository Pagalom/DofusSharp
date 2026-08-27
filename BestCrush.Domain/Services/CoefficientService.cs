using BestCrush.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace BestCrush.Domain.Services;

public class CoefficientService(BestCrushDbContext context,
IDataPriorityProvider dataPriorityProvider)
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
            IsCleared = false,
            ObservedAtUtc = DateTime.UtcNow
        };

        context.CoefficientObservations.Add(observation);
        await context.SaveChangesAsync(cancellationToken);

        return observation;
    }

    public async Task ClearManualAsync(
        long dofusDbId,
        string serverName,
        CancellationToken cancellationToken = default)
    {
        CoefficientObservation observation = new()
        {
            DofusDbId = dofusDbId,
            ServerName = serverName,
            CoefficientPercent = 0,
            Source = CoefficientSource.Manual,
            IsCleared = true,
            ObservedAtUtc = DateTime.UtcNow
        };

        context.CoefficientObservations.Add(observation);

        await context.SaveChangesAsync(
            cancellationToken
        );
    }

    public async Task<
        IReadOnlyDictionary<
            (
                long DofusDbId,
                CoefficientSource Source
            ),
            CoefficientObservation
        >>
        GetLatestLocalSourceObservationsForServerAsync(
            string serverName,
            CancellationToken cancellationToken = default)
    {
        List<CoefficientObservation> observations =
            await context.CoefficientObservations
                .AsNoTracking()
                .Where(observation =>
                    observation.ServerName == serverName &&
                    (
                        observation.Source ==
                            CoefficientSource.Manual ||
                        observation.Source ==
                            CoefficientSource.InGameAutomatic
                    ))
                .OrderByDescending(observation =>
                    observation.ObservedAtUtc)
                .ToListAsync(cancellationToken);

        return observations
            .GroupBy(observation =>
                (
                    observation.DofusDbId,
                    observation.Source
                ))
            .Select(group => group.First())
            .Where(observation =>
                !observation.IsCleared)
            .ToDictionary(
                observation =>
                    (
                        observation.DofusDbId,
                        observation.Source
                    ),
                observation => observation
            );
    }

    private CoefficientObservation? ResolveEffectiveObservation(
        IEnumerable<CoefficientObservation> observations)
    {
        CoefficientObservation[] ordered =
            observations
                .OrderByDescending(c => c.ObservedAtUtc)
                .ToArray();

        CoefficientObservation? latestManual =
            ordered.FirstOrDefault(
                c => c.Source == CoefficientSource.Manual
            );

        CoefficientObservation? manual =
            latestManual is not null &&
            !latestManual.IsCleared
                ? latestManual
                : null;

        CoefficientObservation? game =
            ordered.FirstOrDefault(
                c =>
                    c.Source ==
                        CoefficientSource.InGameAutomatic &&
                    !c.IsCleared
            );

        CoefficientObservation? dofocus =
            ordered.FirstOrDefault(
                c =>
                    c.Source ==
                        CoefficientSource.DofocusInitial &&
                    !c.IsCleared
            );

        if (dataPriorityProvider.Priority ==
            DataPriority.InGameAutomatic)
        {
            return game
                ?? manual
                ?? dofocus;
        }

        return manual
            ?? game
            ?? dofocus;
    }

    public async Task<CoefficientObservation?>
        GetLatestObservationAsync(
            long dofusDbId,
            string serverName,
            CancellationToken cancellationToken = default)
    {
        List<CoefficientObservation> observations =
            await context.CoefficientObservations
                .AsNoTracking()
                .Where(c =>
                    c.DofusDbId == dofusDbId &&
                    c.ServerName == serverName)
                .OrderByDescending(c => c.ObservedAtUtc)
                .ToListAsync(cancellationToken);

        return ResolveEffectiveObservation(
            observations
        );
    }

    public async Task<Dictionary<long, CoefficientObservation>>
        GetLatestObservationsForServerAsync(
            string serverName,
            CancellationToken cancellationToken = default)
    {
        List<CoefficientObservation> observations =
            await context.CoefficientObservations
                .AsNoTracking()
                .Where(c =>
                    c.ServerName == serverName)
                .OrderByDescending(c => c.ObservedAtUtc)
                .ToListAsync(cancellationToken);

        return observations
            .GroupBy(c => c.DofusDbId)
            .Select(group => new
            {
                DofusDbId = group.Key,
                Observation =
                    ResolveEffectiveObservation(group)
            })
            .Where(result =>
                result.Observation is not null)
            .ToDictionary(
                result => result.DofusDbId,
                result => result.Observation!
            );
    }
}
