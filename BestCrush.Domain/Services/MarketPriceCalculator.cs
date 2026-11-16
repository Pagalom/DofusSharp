using BestCrush.Domain.Models;

namespace BestCrush.Domain.Services;

// Pure market pricing algorithms. MarketPriceService retains its public API.
internal static class MarketPriceCalculator
{
    internal static MarketValueResult? CalculateValue(
        long dofusDbId,
        double quantity,
        IReadOnlyDictionary<(long DofusDbId, int Quantity), MarketPriceObservation> observations)
    {
        if (quantity <= 0)
            return new MarketValueResult(0, false);
        if (!double.IsFinite(quantity) || quantity >= (double)long.MaxValue)
            return null;

        var prices = observations.Values.Where(lot => lot.DofusDbId == dofusDbId &&
                lot.Quantity > 0 && lot.Price > 0 && !lot.IsCleared)
            .OrderByDescending(lot => lot.Price).ThenByDescending(lot => lot.Quantity).ToArray();
        if (prices.Length == 0)
            return null;

        long remaining = (long)Math.Floor(quantity);
        double fraction = quantity - remaining;
        bool isEstimated = fraction > 0.000001;
        double value = 0;
        List<MarketResaleLot> lots = [];
        HashSet<MarketPriceObservation> used = [];
        foreach (var lot in prices)
        {
            long count = remaining / lot.Quantity;
            if (count == 0)
                continue;

            long price = MarketPriceService.ResaleLotPrice(lot.Price);
            value += (double)count * price;
            remaining -= count * lot.Quantity;
            lots.Add(new(count, price, lot));
            used.Add(lot);
            if (remaining == 0)
                break;
        }

        if (remaining > 0)
            return null;

        if (isEstimated)
        {
            // A fractional expected rune can only contribute towards a single sale.
            var single = Array.Find(prices, lot => lot.Quantity == 1);
            if (single is null)
                return null;

            value += fraction * MarketPriceService.ResaleLotPrice(single.Price);
            used.Add(single);
        }

        return new MarketValueResult(value, isEstimated, used.ToArray()) { Lots = lots };
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
                    observation.Price > 0 && !observation.IsCleared)
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
