namespace BestCrush.Domain.Models;

public readonly record struct MarketValueResult(
    double Value,
    bool IsEstimated
);