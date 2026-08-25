using BestCrush.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace BestCrush.Domain.Services;

public class MarketPriceService(BestCrushDbContext context)
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
            ObservedAtUtc = DateTime.UtcNow
        };

        context.MarketPriceObservations.Add(observation);
        await context.SaveChangesAsync(cancellationToken);

        return observation;
    }

    public Task<MarketPriceObservation?> GetLatestObservationAsync(
        MarketObjectType objectType,
        long dofusDbId,
        string serverName,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        return context.MarketPriceObservations
            .AsNoTracking()
            .Where(p =>
                p.ObjectType == objectType &&
                p.DofusDbId == dofusDbId &&
                p.ServerName == serverName &&
                p.Quantity == quantity)
            .OrderByDescending(p => p.ObservedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<int, MarketPriceObservation>>
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
            .ToDictionary(
                group => group.Key,
                group => group.First()
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
            .ToDictionary(
                group => group.Key,
                group => group.First()
            );
    }

    public MarketValueResult? CalculateValue(
        long dofusDbId,
        double quantity,
        IReadOnlyDictionary<(long DofusDbId, int Quantity), MarketPriceObservation> observations)
    {
        if (quantity <= 0)
        {
            return new MarketValueResult(0, false);
        }

        List<MarketPriceObservation> prices = observations
            .Where(p => p.Key.DofusDbId == dofusDbId)
            .Select(p => p.Value)
            .ToList();

        if (prices.Count == 0)
        {
            return null;
        }

        double remaining = quantity;
        double value = 0;
        bool isEstimated = false;

        while (remaining >= 1)
        {
            MarketPriceObservation? bestLot = prices
                .Where(p => p.Quantity <= remaining)
                .OrderByDescending(p => (double)p.Price / p.Quantity)
                .ThenByDescending(p => p.Quantity)
                .FirstOrDefault();

            if (bestLot is null)
            {
                break;
            }

            long numberOfLots =
                (long)Math.Floor(remaining / bestLot.Quantity);

            value += numberOfLots * bestLot.Price;

            remaining -= numberOfLots * bestLot.Quantity;
        }

        if (remaining > 0.000001)
        {
            MarketPriceObservation fallback = prices
                .OrderByDescending(p => (double)p.Price / p.Quantity)
                .First();

            double unitPrice =
                (double)fallback.Price / fallback.Quantity;

            value += remaining * unitPrice;
            isEstimated = true;
        }

        return new MarketValueResult(
            value,
            isEstimated
        );
    }
}