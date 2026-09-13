namespace BestCrush.Domain.Models;

public enum MarketAnalyticsPeriod
{
    Hours24,
    Days7,
    Days30,
    All
}

public enum MarketAnalyticsValueMode
{
    UnitPrice,
    LotPrice
}

public enum MarketAnalyticsAggregation
{
    Median,
    Mean,
    FirstQuartile,
    ThirdQuartile
}

public enum MarketAnalyticsSourceMode
{
    InGameAutomatic,
    Manual,
    All
}

public enum MarketAnalyticsTransformation
{
    Raw,
    Base100
}

public sealed class MarketAnalyticsSeriesDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public MarketObjectType ObjectType { get; set; } = MarketObjectType.Rune;
    public long DofusDbId { get; set; }
    public string ItemName { get; set; } = string.Empty;

    // null = tous les lots disponibles.
    public int? LotQuantity { get; set; }

    public MarketAnalyticsValueMode ValueMode { get; set; } =
        MarketAnalyticsValueMode.UnitPrice;

    public MarketAnalyticsAggregation Aggregation { get; set; } =
        MarketAnalyticsAggregation.Median;

    public MarketAnalyticsSourceMode SourceMode { get; set; } =
        MarketAnalyticsSourceMode.InGameAutomatic;

    public MarketAnalyticsTransformation Transformation { get; set; } =
        MarketAnalyticsTransformation.Raw;
}

public sealed class MarketAnalyticsPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public MarketAnalyticsPeriod Period { get; set; } =
        MarketAnalyticsPeriod.Days7;
    public List<MarketAnalyticsSeriesDefinition> Series { get; set; } = [];
}

public sealed record MarketAnalyticsPoint(
    DateTime TimestampUtc,
    double Value,
    int ObservationCount
);

public sealed record MarketAnalyticsSeriesResult(
    Guid SeriesId,
    string Label,
    MarketAnalyticsTransformation Transformation,
    IReadOnlyList<MarketAnalyticsPoint> Points
);
