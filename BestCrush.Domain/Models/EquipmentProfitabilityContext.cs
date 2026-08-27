namespace BestCrush.Domain.Models;

public sealed class EquipmentProfitabilityContext
{
    public required IReadOnlyDictionary<
        (long DofusDbId, int Quantity),
        MarketPriceObservation>
        RunePrices { get; init; }

    public required IReadOnlyDictionary<
        (long DofusDbId, int Quantity),
        MarketPriceObservation>
        EquipmentPrices { get; init; }

    public required IReadOnlyDictionary<
        (long DofusDbId, int Quantity),
        MarketPriceObservation>
        ResourcePrices { get; init; }

    public required Dictionary<
        long,
        CoefficientObservation>
        Coefficients { get; init; }
}