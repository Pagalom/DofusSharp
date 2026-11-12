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

public enum MarketAnalyticsSelectionMode { Single, AllOfType, Criteria, Custom }
public enum MarketAnalyticsGroupAggregation { Mean, Median }
public readonly record struct MarketAnalyticsItemKey(MarketObjectType ObjectType, long DofusDbId);
public sealed record MarketAnalyticsMember(MarketObjectType ObjectType, long DofusDbId, string Name)
{
    public MarketAnalyticsItemKey Key => new(ObjectType, DofusDbId);
}
public sealed record MarketAnalyticsCatalogItem(MarketAnalyticsItemKey Key, string Name, int Level,
    EquipmentType? EquipmentType, IReadOnlyList<Characteristic> Characteristics);

public sealed class MarketAnalyticsSeriesDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public MarketObjectType ObjectType { get; set; } = MarketObjectType.Rune;
    public long DofusDbId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    // Missing fields in v1 saved analyses retain the original single-item behavior.
    public MarketAnalyticsSelectionMode SelectionMode { get; set; }
    public List<MarketAnalyticsMember> Members { get; set; } = [];
    public Characteristic? Characteristic { get; set; }
    public List<EquipmentType> EquipmentTypes { get; set; } = [];
    public int? LevelMin { get; set; }
    public int? LevelMax { get; set; }
    public string? SearchText { get; set; }
    public bool FuseMembers { get; set; } = true;
    public bool IsVisible { get; set; } = true;
    public MarketAnalyticsGroupAggregation GroupAggregation { get; set; }

    // null = tous les lots disponibles.
    public int? LotQuantity { get; set; }

    public MarketAnalyticsValueMode ValueMode { get; set; } =
        MarketAnalyticsValueMode.UnitPrice;

    public MarketAnalyticsAggregation Aggregation { get; set; } =
        MarketAnalyticsAggregation.Median;

    public MarketAnalyticsSourceMode SourceMode { get; set; } =
        MarketAnalyticsSourceMode.InGameAutomatic;

    public MarketAnalyticsTransformation Transformation { get; set; } =
        MarketAnalyticsTransformation.Base100;
}

public sealed class MarketAnalyticsPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public MarketAnalyticsPeriod Period { get; set; } =
        MarketAnalyticsPeriod.Days7;
    public List<MarketAnalyticsSeriesDefinition> Series { get; set; } = [];
    public MarketAnalyticsTransformation? Scale { get; set; }
    public int MaxQuoteAgeHours { get; set; } = 72;
}

public sealed record MarketAnalyticsRequest(string ServerName,
    IReadOnlyList<MarketAnalyticsSeriesDefinition> Series, MarketAnalyticsPeriod Period,
    DateTime? FromUtc = null, DateTime? ToUtc = null, int MaxQuoteAgeHours = 72, int MaxPoints = 160);

public sealed record MarketAnalyticsObservation(MarketAnalyticsItemKey Key, DateTime AtUtc,
    long Price, int Quantity, MarketPriceSource Source, bool IsCleared);

/// <summary>A null value is a real gap. A carried quote never becomes a new observation.</summary>
public sealed record MarketAnalyticsSample(DateTime AtUtc, double? Price, double? Index,
    int ObservationCount, DateTime? LastObservedAtUtc, bool IsCarried);

public sealed record MarketAnalyticsMemberSeries(MarketAnalyticsMember Member,
    IReadOnlyList<MarketAnalyticsSample> Samples, double? ReferencePrice);

public sealed record MarketAnalyticsGroupResult(Guid DefinitionId, string Name,
    IReadOnlyList<MarketAnalyticsMemberSeries> Members,
    IReadOnlyList<MarketAnalyticsSample> FusedSamples, int BaselineMemberCount)
{
    public int CurrentMemberCount => Members.Count(m => m.Samples.LastOrDefault()?.Price is > 0);
}

public sealed record MarketAnalyticsComparison(string ServerName, DateTime FromUtc, DateTime ToUtc,
    DateTime? ReferenceUtc, IReadOnlyList<MarketAnalyticsGroupResult> Groups, int ObservationsRead);

public sealed record MarketAnalyticsPlotSeries(string Key, Guid DefinitionId, string Name,
    IReadOnlyList<MarketAnalyticsSample> Samples);
