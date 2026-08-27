namespace BestCrush.Domain.Models;

public sealed record CraftResourceCostLine(
    long DofusDbId,
    string ResourceName,
    int RequiredQuantity,
    MarketPurchaseResult? Purchase)
{
    public bool HasPrice =>
        Purchase is not null;

    public long? Cost =>
        Purchase?.TotalCost;

    public int? PurchasedQuantity =>
        Purchase?.PurchasedQuantity;

    public int? SurplusQuantity =>
        Purchase?.SurplusQuantity;
}