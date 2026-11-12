using BestCrush.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace BestCrush.Domain.Services;

public class MarketPriceService(BestCrushDbContext context,
    IDataPriorityProvider dataPriorityProvider)
{
    private static readonly TimeSpan
        IdenticalPriceConfirmationInterval =
            MarketPriceReliability.ComparisonWindow;

    public async Task<MarketPriceObservation> AddObservationAsync(
        MarketObjectType objectType,
        long dofusDbId,
        string serverName,
        long price,
        int quantity,
        MarketPriceSource source,
        CancellationToken cancellationToken = default)
    {
        if (price <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(price),
                "Price must be greater than zero."
            );
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Quantity must be greater than zero."
            );
        }

        DateTime observedAtUtc =
            DateTime.UtcNow;

        MarketPriceObservation? latest =
            await context.MarketPriceObservations
                .AsNoTracking()
                .Where(observation =>
                    observation.ObjectType == objectType &&
                    observation.DofusDbId == dofusDbId &&
                    observation.ServerName == serverName &&
                    observation.Quantity == quantity &&
                    observation.Source == source)
                .OrderByDescending(observation =>
                    observation.ObservedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

        if (latest is not null &&
            !latest.IsCleared &&
            latest.Price == price &&
            observedAtUtc - latest.ObservedAtUtc <
                IdenticalPriceConfirmationInterval)
        {
            return latest;
        }

        MarketPriceObservation observation = new()
        {
            ObjectType = objectType,
            DofusDbId = dofusDbId,
            ServerName = serverName,
            Price = price,
            Quantity = quantity,
            Source = source,
            IsCleared = false,
            ObservedAtUtc = observedAtUtc
        };

        context.MarketPriceObservations.Add(observation);
        await context.SaveChangesAsync(cancellationToken);

        return observation;
    }

    public async Task<IReadOnlyList<int>> RecordAutomaticLotSnapshotAsync(
        MarketObjectType objectType,
        long dofusDbId,
        string serverName,
        IReadOnlyDictionary<int, long?> lotPrices,
        CancellationToken cancellationToken)
    {
        var previous = await context.MarketPriceObservations.AsNoTracking()
            .Where(quote => quote.ObjectType == objectType && quote.DofusDbId == dofusDbId &&
                quote.ServerName == serverName)
            .GroupBy(quote => new { quote.Quantity, quote.Source })
            .Select(group => group.OrderByDescending(quote => quote.ObservedAtUtc).First())
            .ToListAsync(cancellationToken);
        var latest = previous.ToDictionary(quote => (quote.Quantity, quote.Source));
        var now = DateTime.UtcNow;
        List<int> reportedLots = [];
        foreach (var (quantity, price) in lotPrices)
        {
            var automatic = latest.GetValueOrDefault((quantity, MarketPriceSource.InGameAutomatic));
            var manual = latest.GetValueOrDefault((quantity, MarketPriceSource.Manual));
            if (price is null)
            {
                bool manualStillCurrent = manual is { IsCleared: false } &&
                    (automatic is null || manual.ObservedAtUtc > automatic.ObservedAtUtc);
                if (automatic is not { IsCleared: false } && !manualStillCurrent)
                    continue;
            }

            reportedLots.Add(quantity);
            if (price is not null && automatic is { IsCleared: false } &&
                automatic.Price == price && now - automatic.ObservedAtUtc < IdenticalPriceConfirmationInterval)
                continue;

            context.MarketPriceObservations.Add(new MarketPriceObservation
            {
                ObjectType = objectType,
                DofusDbId = dofusDbId,
                ServerName = serverName,
                Quantity = quantity,
                Source = MarketPriceSource.InGameAutomatic,
                Price = price ?? 0,
                IsCleared = price is null,
                ObservedAtUtc = now
            });
        }

        await context.SaveChangesAsync(cancellationToken);
        return reportedLots;
    }

    public async Task ClearLocalAsync(
        MarketObjectType objectType,
        long dofusDbId,
        string serverName,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        DateTime observedAtUtc =
            DateTime.UtcNow;

        MarketPriceObservation manualClear = new()
        {
            ObjectType = objectType,
            DofusDbId = dofusDbId,
            ServerName = serverName,
            Price = 0,
            Quantity = quantity,
            Source = MarketPriceSource.Manual,
            IsCleared = true,
            ObservedAtUtc = observedAtUtc
        };

        MarketPriceObservation gameClear = new()
        {
            ObjectType = objectType,
            DofusDbId = dofusDbId,
            ServerName = serverName,
            Price = 0,
            Quantity = quantity,
            Source = MarketPriceSource.InGameAutomatic,
            IsCleared = true,
            ObservedAtUtc = observedAtUtc
        };

        context.MarketPriceObservations.AddRange(
            manualClear,
            gameClear
        );

        await context.SaveChangesAsync(
            cancellationToken
        );
    }

    public async Task<
        IReadOnlyDictionary<
            (
                long DofusDbId,
                int Quantity,
                MarketPriceSource Source
            ),
            MarketPriceObservation
        >>
        GetLatestLocalSourceObservationsForServerAsync(
            MarketObjectType objectType,
            string serverName,
            CancellationToken cancellationToken = default)
    {
        List<MarketPriceObservation> observations =
            await context.MarketPriceObservations
                .AsNoTracking()
                .Where(observation =>
                    observation.ObjectType == objectType &&
                    observation.ServerName == serverName &&
                    (
                        observation.Source ==
                            MarketPriceSource.Manual ||
                        observation.Source ==
                            MarketPriceSource.InGameAutomatic
                    ))
                .OrderByDescending(observation =>
                    observation.ObservedAtUtc)
                .ToListAsync(cancellationToken);

        var latest = observations
            .GroupBy(observation => (observation.DofusDbId, observation.Quantity, observation.Source))
            .Select(group => group.First())
            .Where(observation => !observation.IsCleared);
        var rejected = MarketPriceReliability.GetRejectedHistoryIds(observations);

        return MarketPriceReliability.FilterCurrent(latest, rejected)
            .ToDictionary(
                observation =>
                    (
                        observation.DofusDbId,
                        observation.Quantity,
                        observation.Source
                    ),
                observation => observation
            );
    }

    private MarketPriceObservation? ResolveEffectiveObservation(
        IEnumerable<MarketPriceObservation> observations,
        bool excludeUnavailableLots)
    {
        MarketPriceObservation[] ordered =
            observations
                .OrderByDescending(p => p.ObservedAtUtc)
                .ToArray();

        MarketPriceObservation? latestManual =
            ordered.FirstOrDefault(
                p => p.Source == MarketPriceSource.Manual
            );

        MarketPriceObservation? manual =
            latestManual is not null &&
            !latestManual.IsCleared
                ? latestManual
                : null;

        MarketPriceObservation? latestGame =
            ordered.FirstOrDefault(
                p =>
                    p.Source ==
                        MarketPriceSource.InGameAutomatic
            );

        if (excludeUnavailableLots && latestGame is { IsCleared: true } &&
            (manual is null || manual.ObservedAtUtc <= latestGame.ObservedAtUtc))
            return null;

        MarketPriceObservation? game =
            latestGame is not null &&
            !latestGame.IsCleared
                ? latestGame
                : null;

        if (dataPriorityProvider.Priority ==
            DataPriority.InGameAutomatic)
        {
            return game
                ?? manual;
        }

        return manual
            ?? game;
    }

    public async Task<MarketPriceObservation?> GetLatestObservationAsync(
        MarketObjectType objectType,
        long dofusDbId,
        string serverName,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        List<MarketPriceObservation> observations =
            await context.MarketPriceObservations
                .AsNoTracking()
                .Where(p =>
                    p.ObjectType == objectType &&
                    p.DofusDbId == dofusDbId &&
                    p.ServerName == serverName)
                .OrderByDescending(p => p.ObservedAtUtc)
                .ToListAsync(cancellationToken);

        return ResolveCurrentObservations(observations)
            .FirstOrDefault(quote => quote.Quantity == quantity);
    }

    public async Task<IReadOnlyDictionary<(long DofusDbId, int Quantity), MarketPriceObservation>>
        GetLatestObservationsForServerAsync(
            MarketObjectType objectType,
            string serverName,
            CancellationToken cancellationToken = default,
            bool excludeUnavailableLots = false)
    {
        List<MarketPriceObservation> observations =
            await context.MarketPriceObservations
                .AsNoTracking()
                .Where(p =>
                    p.ObjectType == objectType &&
                    p.ServerName == serverName)
                .OrderByDescending(p => p.ObservedAtUtc)
                .ToListAsync(cancellationToken);

        return ResolveCurrentObservations(observations, excludeUnavailableLots)
            .ToDictionary(quote => (quote.DofusDbId, quote.Quantity));
    }

    private IEnumerable<MarketPriceObservation> ResolveCurrentObservations(
        IReadOnlyList<MarketPriceObservation> observations, bool excludeUnavailableLots = false)
    {
        var latest = observations.GroupBy(quote => (quote.DofusDbId, quote.Quantity))
            .Select(group => ResolveEffectiveObservation(group, excludeUnavailableLots))
            .OfType<MarketPriceObservation>();
        return MarketPriceReliability.FilterCurrent(latest, MarketPriceReliability.GetRejectedHistoryIds(observations));
    }

    public async Task<IReadOnlyList<MarketPriceObservation>>
        GetHistoryAsync(
            MarketObjectType objectType,
            long dofusDbId,
            string serverName,
            DateTime? fromUtc = null,
            DateTime? toUtc = null,
            MarketPriceSource? source = null,
            int? quantity = null,
            CancellationToken cancellationToken = default)
    {
        if (fromUtc.HasValue &&
            toUtc.HasValue &&
            fromUtc.Value > toUtc.Value)
        {
            throw new ArgumentException(
                "fromUtc must be earlier than or equal to toUtc."
            );
        }

        IQueryable<MarketPriceObservation> query =
            context.MarketPriceObservations
                .AsNoTracking()
                .Where(observation =>
                    observation.ObjectType == objectType &&
                    observation.DofusDbId == dofusDbId &&
                    observation.ServerName == serverName);

        if (objectType is MarketObjectType.Resource or MarketObjectType.Rune && source != MarketPriceSource.Manual)
        {
            var rejected = await MarketPriceReliability.GetRejectedHistoryIdsAsync(query, fromUtc, toUtc, cancellationToken);
            query = query.Where(observation => !rejected.Contains(observation.Id));
        }

        query = query.Where(observation => !observation.IsCleared && observation.Price > 0);

        if (fromUtc.HasValue)
        {
            query = query.Where(observation =>
                observation.ObservedAtUtc >=
                    fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(observation =>
                observation.ObservedAtUtc <=
                    toUtc.Value);
        }

        if (source.HasValue)
        {
            query = query.Where(observation =>
                observation.Source ==
                    source.Value);
        }

        if (quantity.HasValue)
        {
            if (quantity.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(quantity),
                    "Quantity must be greater than zero."
                );
            }

            query = query.Where(observation =>
                observation.Quantity ==
                    quantity.Value);
        }

        return await query
            .OrderBy(observation =>
                observation.ObservedAtUtc)
            .ThenBy(observation =>
                observation.Quantity)
            .ThenBy(observation =>
                observation.Source)
            .ToListAsync(cancellationToken);
    }

    public MarketValueResult? CalculateValue(
        long dofusDbId,
        double quantity,
        IReadOnlyDictionary<(long DofusDbId, int Quantity), MarketPriceObservation> observations,
        long lotPriceReduction = 0)
    {
        return MarketPriceCalculator.CalculateValue(dofusDbId, quantity, observations, lotPriceReduction);
    }

    public MarketPurchaseResult? CalculateMinimumPurchaseCost(
        long dofusDbId,
        int requiredQuantity,
        IReadOnlyDictionary<(long DofusDbId, int Quantity), MarketPriceObservation> observations)
    {
        return MarketPriceCalculator.CalculateMinimumPurchaseCost(dofusDbId, requiredQuantity, observations);
    }
}
