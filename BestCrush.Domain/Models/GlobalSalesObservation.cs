namespace BestCrush.Domain.Models;

/// <summary>A market is scoped to a server and a sellable variant, never to a character.</summary>
public sealed record SalesMarketKey
{
    public string ServerName { get; }
    public MarketObjectType ObjectType { get; }
    public long ItemId { get; }
    public string Variant { get; }

    public SalesMarketKey(string serverName, MarketObjectType objectType, long itemId, string variant = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        ArgumentNullException.ThrowIfNull(variant);
        if (serverName.Trim().Length > 64) throw new ArgumentOutOfRangeException(nameof(serverName));
        if (!Enum.IsDefined(objectType)) throw new ArgumentOutOfRangeException(nameof(objectType));
        if (itemId <= 0) throw new ArgumentOutOfRangeException(nameof(itemId));
        if (variant.Length > 512 || variant.Any(char.IsControl)) throw new ArgumentOutOfRangeException(nameof(variant));
        ServerName = serverName.Trim().ToUpperInvariant();
        ObjectType = objectType;
        ItemId = itemId;
        Variant = variant;
    }
}

/// <summary>
/// A complete period of actual global sales, expressed in ITEM UNITS, not lots,
/// listings, price observations or this player's transactions. Ingestion must
/// validate that meaning before constructing an observation. Zero is a known
/// absence of sales; an unknown count must not be ingested as zero.
/// </summary>
public sealed record GlobalSalesObservation
{
    public SalesMarketKey Market { get; }
    public DateTimeOffset PeriodStartUtc { get; }
    public DateTimeOffset PeriodEndUtc { get; }
    public long UnitsSold { get; }
    public DateTimeOffset ObservedAtUtc { get; }

    public GlobalSalesObservation(SalesMarketKey market, DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc, long unitsSold, DateTimeOffset observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(market);
        if (periodEndUtc <= periodStartUtc) throw new ArgumentOutOfRangeException(nameof(periodEndUtc));
        if (unitsSold < 0) throw new ArgumentOutOfRangeException(nameof(unitsSold));
        if (periodEndUtc > observedAtUtc) throw new ArgumentOutOfRangeException(nameof(observedAtUtc));
        Market = market;
        PeriodStartUtc = periodStartUtc.ToUniversalTime();
        PeriodEndUtc = periodEndUtc.ToUniversalTime();
        UnitsSold = unitsSold;
        ObservedAtUtc = observedAtUtc.ToUniversalTime();
    }
}

public enum SalesKineticsStatus { Missing, Outdated, NoObservedSales, Available }

public sealed record SalesKinetics(SalesKineticsStatus Status, double? UnitsPerDay,
    double? MarketAbsorptionDays, GlobalSalesObservation? Evidence)
{
    // This is a market volume comparison, NOT the predicted waiting time for
    // an individual listing. Competition and the chosen price remain unknown.
    public bool HasMeasuredDemand => Status == SalesKineticsStatus.Available;
}

public sealed record SaleProfitability(double? Revenue, double? Cost, double? Gain,
    double? RoiPercent, SalesKinetics Kinetics, double? MarketDailyGainCapacity)
{
    public bool HasCompletePrices => Revenue is not null && Cost is not null;
    public bool CanCompareKinetics => HasCompletePrices && Kinetics.HasMeasuredDemand;
}
