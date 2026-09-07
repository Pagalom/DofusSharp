namespace BestCrush.Domain.Models;

public class CrushHistoryRuneLot
{
#pragma warning disable CS8618
    public CrushHistoryRuneLot() { }
#pragma warning restore CS8618

    public CrushHistoryRuneLot(
        CrushHistoryRune rune,
        long count,
        int lotQuantity,
        long lotPrice,
        bool isEstimated,
        MarketPriceSource? priceSource,
        DateTime? priceObservedAtUtc)
    {
        Rune = rune;
        Count = count;
        LotQuantity = lotQuantity;
        LotPrice = lotPrice;
        IsEstimated = isEstimated;
        PriceSource = priceSource;
        PriceObservedAtUtc = priceObservedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid RuneId { get; private set; }
    public CrushHistoryRune Rune { get; private set; }

    public long Count { get; private set; }
    public int LotQuantity { get; private set; }
    public long LotPrice { get; private set; }
    public bool IsEstimated { get; private set; }

    public MarketPriceSource? PriceSource { get; private set; }
    public DateTime? PriceObservedAtUtc { get; private set; }
}
