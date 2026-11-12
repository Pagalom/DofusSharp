using BestCrush.Domain.Models;

namespace BestCrush.Domain.Services;

/// <summary>Pure calculations shared by resale, captured souls and harvested resources.</summary>
public static class SalesKineticsCalculator
{
    public static SalesKinetics Evaluate(SalesMarketKey market, long offeredUnits,
        GlobalSalesObservation? observation, DateTimeOffset now, TimeSpan maximumAge)
    {
        ArgumentNullException.ThrowIfNull(market);
        if (offeredUnits <= 0) throw new ArgumentOutOfRangeException(nameof(offeredUnits));
        if (maximumAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumAge));
        if (observation is null || observation.Market != market)
            return new(SalesKineticsStatus.Missing, null, null, null);
        // A recently re-opened OLD period is still old. Freshness is based on
        // the period's end as well as its capture time, never capture time alone.
        if (observation.ObservedAtUtc > now || observation.PeriodEndUtc > now ||
            now - observation.PeriodEndUtc > maximumAge || now - observation.ObservedAtUtc > maximumAge)
            return new(SalesKineticsStatus.Outdated, null, null, observation);
        double dailyUnits = observation.UnitsSold /
            (observation.PeriodEndUtc - observation.PeriodStartUtc).TotalDays;
        if (!double.IsFinite(dailyUnits))
            return new(SalesKineticsStatus.Outdated, null, null, observation);
        return observation.UnitsSold == 0
            ? new(SalesKineticsStatus.NoObservedSales, 0, null, observation)
            : new(SalesKineticsStatus.Available, dailyUnits, offeredUnits / dailyUnits, observation);
    }

    public static SaleProfitability EvaluateProfitability(SalesMarketKey market, long offeredUnits,
        double? revenue, double? cost, GlobalSalesObservation? observation,
        DateTimeOffset now, TimeSpan maximumAge)
    {
        ValidateAmount(revenue, nameof(revenue));
        ValidateAmount(cost, nameof(cost));
        var kinetics = Evaluate(market, offeredUnits, observation, now, maximumAge);
        double? gain = revenue - cost;
        double? roi = cost is > 0 ? gain / cost * 100 : null;
        // Indicative market-wide capacity at the observed demand, assuming
        // the entire market's demand. This is not expected personal income,
        // a sale probability, an ETA, or income per hour spent fighting.
        double? capacity = kinetics.MarketAbsorptionDays is > 0
            ? gain / kinetics.MarketAbsorptionDays : null;
        return new(revenue, cost, FiniteOrNull(gain), FiniteOrNull(roi), kinetics, FiniteOrNull(capacity));
    }

    private static void ValidateAmount(double? amount, string name)
    {
        if (amount is { } value && (!double.IsFinite(value) || value < 0))
            throw new ArgumentOutOfRangeException(name);
    }
    private static double? FiniteOrNull(double? value) => value is { } number && double.IsFinite(number) ? number : null;
}
