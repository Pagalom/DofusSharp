using BestCrush.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace BestCrush.Domain.Services;

public class MarketPriceService(BestCrushDbContext context,
    IDataPriorityProvider dataPriorityProvider)
{
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

        MarketPriceObservation observation = new()
        {
            ObjectType = objectType,
            DofusDbId = dofusDbId,
            ServerName = serverName,
            Price = price,
            Quantity = quantity,
            Source = source,
            IsCleared = false,
            ObservedAtUtc = DateTime.UtcNow
        };

        context.MarketPriceObservations.Add(observation);
        await context.SaveChangesAsync(cancellationToken);

        return observation;
    }

    public async Task ClearManualAsync(
        MarketObjectType objectType,
        long dofusDbId,
        string serverName,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        MarketPriceObservation observation = new()
        {
            ObjectType = objectType,
            DofusDbId = dofusDbId,
            ServerName = serverName,
            Price = 0,
            Quantity = quantity,
            Source = MarketPriceSource.Manual,
            IsCleared = true,
            ObservedAtUtc = DateTime.UtcNow
        };

        context.MarketPriceObservations.Add(observation);

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

        return observations
            .GroupBy(observation =>
                (
                    observation.DofusDbId,
                    observation.Quantity,
                    observation.Source
                ))
            .Select(group => group.First())
            .Where(observation =>
                !observation.IsCleared)
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
        IEnumerable<MarketPriceObservation> observations)
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

        MarketPriceObservation? game =
            ordered.FirstOrDefault(
                p =>
                    p.Source ==
                        MarketPriceSource.InGameAutomatic &&
                    !p.IsCleared
            );

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
                    p.ServerName == serverName &&
                    p.Quantity == quantity)
                .OrderByDescending(p => p.ObservedAtUtc)
                .ToListAsync(cancellationToken);

        return ResolveEffectiveObservation(
            observations
        );
    }

    public async Task<Dictionary<int, MarketPriceObservation>>
        GetLatestObservationsAsync(
            MarketObjectType objectType,
            long dofusDbId,
            string serverName,
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

        return observations
            .GroupBy(p => p.Quantity)
            .Select(group => new
            {
                Quantity = group.Key,
                Observation =
                    ResolveEffectiveObservation(group)
            })
            .Where(result =>
                result.Observation is not null)
            .ToDictionary(
                result => result.Quantity,
                result => result.Observation!
            );
    }
    public async Task<double?> GetLatestUnitPriceAsync(
        MarketObjectType objectType,
        long dofusDbId,
        string serverName,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        MarketPriceObservation? observation =
            await GetLatestObservationAsync(
                objectType,
                dofusDbId,
                serverName,
                quantity,
                cancellationToken
            );

        if (observation is null)
        {
            return null;
        }

        return (double)observation.Price / observation.Quantity;
    }
    public async Task<IReadOnlyDictionary<(long DofusDbId, int Quantity), MarketPriceObservation>>
        GetLatestObservationsForServerAsync(
            MarketObjectType objectType,
            string serverName,
            CancellationToken cancellationToken = default)
    {
        List<MarketPriceObservation> observations =
            await context.MarketPriceObservations
                .AsNoTracking()
                .Where(p =>
                    p.ObjectType == objectType &&
                    p.ServerName == serverName)
                .OrderByDescending(p => p.ObservedAtUtc)
                .ToListAsync(cancellationToken);

        return observations
            .GroupBy(p => (p.DofusDbId, p.Quantity))
            .Select(group => new
            {
                group.Key,
                Observation =
                    ResolveEffectiveObservation(group)
            })
            .Where(result =>
                result.Observation is not null)
            .ToDictionary(
                result => result.Key,
                result => result.Observation!
            );
    }

    public MarketValueResult? CalculateValue(
        long dofusDbId,
        double quantity,
        IReadOnlyDictionary<
            (long DofusDbId, int Quantity),
            MarketPriceObservation> observations)
    {
        if (quantity <= 0)
        {
            return new MarketValueResult(
                0,
                false
            );
        }

        MarketPriceObservation[] prices =
            observations
                .Where(entry =>
                    entry.Key.DofusDbId ==
                        dofusDbId)
                .Select(entry =>
                    entry.Value)
                .Where(observation =>
                    observation.Quantity > 0 &&
                    observation.Price > 0)
                .OrderByDescending(observation =>
                    observation.Quantity)
                .ToArray();

        if (prices.Length == 0)
        {
            return null;
        }

        long wholeQuantity =
            (long)Math.Floor(
                quantity
            );

        double fractionalQuantity =
            quantity -
            wholeQuantity;

        long remainingWhole =
            wholeQuantity;

        double value = 0;

        bool isEstimated =
            fractionalQuantity > 0.000001;

        HashSet<MarketPriceObservation>
            usedObservations = [];

        // On vend les runes entières comme
        // un joueur le ferait réellement :
        //
        // x1000 → x100 → x10 → x1
        //
        // On privilégie donc toujours le
        // plus gros lot disponible.
        foreach (
            MarketPriceObservation lot
            in prices)
        {
            if (remainingWhole <
                lot.Quantity)
            {
                continue;
            }

            long numberOfLots =
                remainingWhole /
                lot.Quantity;

            if (numberOfLots <= 0)
            {
                continue;
            }

            value +=
                numberOfLots *
                lot.Price;

            remainingWhole -=
                numberOfLots *
                lot.Quantity;

            usedObservations.Add(
                lot
            );

            if (remainingWhole == 0)
            {
                break;
            }
        }

        // Normalement, avec un prix x1,
        // remainingWhole vaut toujours 0.
        //
        // Si un type de lot manque, on conserve
        // malgré tout une estimation en utilisant
        // le plus petit lot disponible.
        if (remainingWhole > 0)
        {
            MarketPriceObservation fallback =
                prices
                    .OrderBy(observation =>
                        observation.Quantity)
                    .First();

            double unitPrice =
                (double)fallback.Price /
                fallback.Quantity;

            value +=
                remainingWhole *
                unitPrice;

            usedObservations.Add(
                fallback
            );

            isEstimated = true;
        }

        // Le résultat du concassage est une
        // espérance mathématique et peut donc
        // produire une fraction de rune.
        //
        // Cette fraction est valorisée au prix
        // unitaire x1 lorsqu'il existe.
        if (fractionalQuantity >
            0.000001)
        {
            MarketPriceObservation fractionalPrice =
                prices.FirstOrDefault(
                    observation =>
                        observation.Quantity == 1
                )
                ?? prices
                    .OrderBy(observation =>
                        observation.Quantity)
                    .First();

            double unitPrice =
                (double)fractionalPrice.Price /
                fractionalPrice.Quantity;

            value +=
                fractionalQuantity *
                unitPrice;

            usedObservations.Add(
                fractionalPrice
            );
        }

        return new MarketValueResult(
            value,
            isEstimated,
            usedObservations.ToArray()
        );
    }

    public MarketPurchaseResult?
        CalculateMinimumPurchaseCost(
            long dofusDbId,
            int requiredQuantity,
            IReadOnlyDictionary<
                (long DofusDbId, int Quantity),
                MarketPriceObservation> observations)
    {
        if (requiredQuantity <= 0)
        {
            return new MarketPurchaseResult(
                0,
                requiredQuantity,
                0,
                new Dictionary<int, int>()
            );
        }

        MarketPriceObservation[] prices =
            observations
                .Where(entry =>
                    entry.Key.DofusDbId ==
                        dofusDbId)
                .Select(entry =>
                    entry.Value)
                .Where(observation =>
                    observation.Quantity > 0 &&
                    observation.Price > 0)
                .OrderBy(observation =>
                    observation.Quantity)
                .ToArray();

        if (prices.Length == 0)
        {
            return null;
        }

        int maximumLot =
            prices.Max(observation =>
                observation.Quantity);

        int maximumQuantity =
            checked(
                requiredQuantity +
                maximumLot -
                1
            );

        long?[] costs =
            new long?[maximumQuantity + 1];

        int[] previousQuantity =
            new int[maximumQuantity + 1];

        int[] previousLot =
            new int[maximumQuantity + 1];

        costs[0] = 0;

        for (int quantity = 0;
            quantity <= maximumQuantity;
            quantity++)
        {
            if (costs[quantity] is not long currentCost)
            {
                continue;
            }

            foreach (
                MarketPriceObservation lot
                in prices)
            {
                int nextQuantity =
                    quantity +
                    lot.Quantity;

                if (nextQuantity >
                    maximumQuantity)
                {
                    continue;
                }

                long nextCost =
                    checked(
                        currentCost +
                        lot.Price
                    );

                if (costs[nextQuantity] is null ||
                    nextCost <
                    costs[nextQuantity]!.Value)
                {
                    costs[nextQuantity] =
                        nextCost;

                    previousQuantity[nextQuantity] =
                        quantity;

                    previousLot[nextQuantity] =
                        lot.Quantity;
                }
            }
        }

        int? bestQuantity = null;
        long? bestCost = null;

        for (int quantity = requiredQuantity;
            quantity <= maximumQuantity;
            quantity++)
        {
            if (costs[quantity] is not long cost)
            {
                continue;
            }

            if (bestCost is null ||
                cost < bestCost.Value ||
                (
                    cost == bestCost.Value &&
                    quantity <
                    bestQuantity!.Value
                ))
            {
                bestCost = cost;
                bestQuantity = quantity;
            }
        }

        if (bestQuantity is null ||
            bestCost is null)
        {
            return null;
        }

        Dictionary<int, int> lots = [];

        int currentQuantity =
            bestQuantity.Value;

        while (currentQuantity > 0)
        {
            int lotQuantity =
                previousLot[currentQuantity];

            if (lotQuantity <= 0)
            {
                return null;
            }

            if (lots.TryGetValue(
                lotQuantity,
                out int count))
            {
                lots[lotQuantity] =
                    count + 1;
            }
            else
            {
                lots[lotQuantity] = 1;
            }

            currentQuantity =
                previousQuantity[
                    currentQuantity
                ];
        }

        Dictionary<int, MarketPriceObservation>
            observationsByQuantity =
                prices.ToDictionary(
                    observation =>
                        observation.Quantity
                );

        List<MarketPriceObservation>
            usedObservations = [];

        foreach (int lotQuantity in lots.Keys)
        {
            if (observationsByQuantity.TryGetValue(
                lotQuantity,
                out MarketPriceObservation? observation))
            {
                usedObservations.Add(
                    observation
                );
            }
        }

        return new MarketPurchaseResult(
            bestCost.Value,
            requiredQuantity,
            bestQuantity.Value,
            lots,
            usedObservations
        );
    }
}