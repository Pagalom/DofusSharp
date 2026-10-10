using BestCrush.Domain.Models;

namespace BestCrush.Domain.Services;

// Pure market pricing algorithms. MarketPriceService retains its public API.
internal static class MarketPriceCalculator
{
    internal static MarketValueResult? CalculateValue(
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

    internal static MarketPurchaseResult?
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
