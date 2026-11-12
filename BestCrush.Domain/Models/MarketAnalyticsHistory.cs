namespace BestCrush.Domain.Models;

public sealed record MarketAnalyticsHistoryRow(Guid Id, DateTime AtUtc, long Price,
    int Quantity, MarketPriceSource Source, bool IsCleared)
{
    public double? UnitPrice => !IsCleared && Quantity > 0 && Price > 0 ? Price / (double)Quantity : null;
}

public sealed record MarketAnalyticsHistoryPage(IReadOnlyList<MarketAnalyticsHistoryRow> Rows, bool HasMore);
