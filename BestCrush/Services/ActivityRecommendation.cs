using BestCrush.Domain.Models;

namespace BestCrush.Services;

public sealed record CraftAlternative(long ItemId, string Name, string Recipe, double AdditionalGain,
    string? Job = null, int? RequiredLevel = null)
{
    public long? IconId { get; init; }
    public IReadOnlyList<TradeRecipeIngredient> Ingredients { get; init; } = [];
}

public sealed record TradeRecipeIngredient(long ItemId, string Name, int Quantity, long? IconId);
// Presentation of the exact lots already used by the analysis; no prices are recomputed here.
public sealed record TradingLotPresentation(int? BuyQuantity = null, double? BuyPrice = null,
    int? SellQuantity = null, double? SellPrice = null, bool HistoricalResale = false);

public sealed record StockResaleTarget(int LotSize, double MinimumPrice, bool UsesMedian);

public sealed record SoulCapturePresentation(string Dungeon, EmptySoulStone? Stone,
    bool EstimatedStoneCost, bool EstimatedResale);

public sealed record FarmingTransformation(string ResourceName, int ResourcesUsed, int OutputQuantity);

public readonly record struct TradingPriceTargets(double MaximumPurchase, double MinimumResale)
{
    public static TradingPriceTargets Calculate(double purchase, double estimatedResale, double targetRoiPercent)
    {
        decimal multiplier = 1 + (decimal)targetRoiPercent / 100;
        decimal maximumPurchase = Math.Min(decimal.Floor((decimal)estimatedResale / multiplier), (decimal)estimatedResale - 1);
        decimal minimumResale = Math.Max(decimal.Ceiling((decimal)purchase * multiplier), (decimal)purchase + 1);
        return new((double)maximumPurchase, (double)minimumResale);
    }
}

public sealed record HistoricalPriceComparison(double CurrentLotPrice, double MedianLotPrice, DateTimeOffset? LastAtOrAboveMedianUtc)
{
    public double DiscountPercent => 100 * (MedianLotPrice - CurrentLotPrice) / MedianLotPrice;

    public int? DaysSinceMedian(DateTimeOffset now) => LastAtOrAboveMedianUtc is { } at
        ? Math.Max(0, (int)(now - at).TotalDays)
        : null;
}

public sealed record ActivityRecommendation(string Id, string Module, string Strategy, long ItemId,
    string Name, int Level, MarketObjectType ObjectType, long Quantity, double? Revenue, double? Cost,
    SaleProfitability Profitability, IReadOnlyList<string> MissingData, IReadOnlyList<string> Details)
{
    public long? IconId { get; init; }
    public TradingLotPresentation? TradingLots { get; init; }
    public HistoricalPriceComparison? HistoricalPrice { get; init; }
    public IReadOnlyList<StockResaleTarget> StockResaleTargets { get; init; } = [];
    public SoulCapturePresentation? SoulCapture { get; init; }
    public FarmingTransformation? FarmingCraft { get; init; }
    public ResaleQuota? ResaleQuota { get; init; }
    public bool UsesResaleVolumeCeiling { get; init; }
    public double? CashRequired { get; init; }
    public double? Spending => CashRequired ?? Cost;
    public bool IsTradingPurchase => TradingLots?.BuyQuantity is > 0;
    public bool MeetsTradingTarget(double targetRoiPercent) => !IsTradingPurchase ||
        (Cost is > 0 && Revenue is { } resale &&
         Cost.Value <= TradingPriceTargets.Calculate(Cost.Value, resale, targetRoiPercent).MaximumPurchase);
    public string? Job { get; init; }
    public int? RequiredJobLevel { get; init; }
    public string? CraftJob { get; init; }
    public int? RequiredCraftLevel { get; init; }
    public IReadOnlyList<CraftAlternative> Crafts { get; init; } = [];
    public IReadOnlyList<long> MonsterIds { get; init; } = [];
    public long? BossId { get; init; }
    public DateTime? PriceUpdatedUtc { get; init; }
    public string? SourceHref { get; init; }
    public string OriginName => Module switch { "craft" => "Craft", "concassage" => "Concassage", "achat-vente" => "Achat / Vente", "pierres-ame" => "Pierres d’âme", _ => "Farming" };
    public bool Complete => MissingData.Count == 0;
    public string SourceUrl => SourceHref ?? $"/modules/{Module}?item={ItemId}";
}

public sealed record ActivityReport(string Server, string? StockSourceId, DateTime CalculatedUtc,
    IReadOnlyList<ActivityRecommendation> Candidates, IReadOnlyList<string> Notices);

public sealed record SoulCaptureQuote(string Server, long BossId, double Price, DateTimeOffset ObservedAtUtc)
{
    public bool IsAutomatic { get; init; }
    public long? ItemId { get; init; }
    public int LotQuantity { get; init; } = 1;
    public long? LotPrice { get; init; }
}
