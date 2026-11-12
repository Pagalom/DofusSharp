using BestCrush.Domain.Models;

namespace BestCrush.Services;

internal sealed class TradingResaleDemand
{
    private readonly GlobalSalePoint[] _hours;
    private readonly long _typicalHourlyVolume;

    private TradingResaleDemand(GlobalSalePoint[] hours)
    {
        _hours = hours;
        // Use the lower median, so an isolated burst cannot qualify a large lot.
        _typicalHourlyVolume = hours.Select(hour => hour.UnitsSold).Order().ElementAtOrDefault((hours.Length - 1) / 2);
    }

    internal static TradingResaleDemand? FromHistory(GlobalSalesHistory? history, DateTimeOffset now, TimeSpan maximumAge)
    {
        if (history is null || history.ObservedAtUtc > now || now - history.ObservedAtUtc > maximumAge)
            return null;

        return new(history.Points(1).Where(hour => hour.RecordedAtUtc <= history.ObservedAtUtc && hour.UnitsSold > 0).ToArray());
    }

    internal long CompatibleUnits(int lotSize, double lotPrice)
    {
        if (lotSize > 1 && lotSize > _typicalHourlyVolume)
            return 0;

        double unitPrice = lotPrice / lotSize;
        long units = 0;
        int compatibleHours = 0;
        foreach (var hour in _hours)
        {
            if (hour.UnitPrice < unitPrice || hour.UnitsSold < lotSize)
                continue;

            // Each hourly total is only an upper bound on whole lots sold.
            units += hour.UnitsSold / lotSize * lotSize;
            compatibleHours++;
        }

        return lotSize == 1 || compatibleHours >= 2 ? units : 0;
    }
}
