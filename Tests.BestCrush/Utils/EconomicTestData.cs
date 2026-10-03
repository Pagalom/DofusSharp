using BestCrush.Domain.Models;
using BestCrush.Domain.Services;

namespace Tests.BestCrush.Utils;

internal static class EconomicTestData
{
    public const string Server = "TEST_SERVER";
    public static readonly DateTime ObservedAt = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public static MarketPriceObservation Price(
        long id, int quantity, long price,
        MarketPriceSource source = MarketPriceSource.InGameAutomatic,
        string server = Server,
        MarketObjectType type = MarketObjectType.Resource,
        DateTime? at = null,
        bool cleared = false) => new()
        {
            DofusDbId = id,
            Quantity = quantity,
            Price = price,
            Source = source,
            ServerName = server,
            ObjectType = type,
            ObservedAtUtc = at ?? ObservedAt,
            IsCleared = cleared
        };

    public static IReadOnlyDictionary<(long DofusDbId, int Quantity), MarketPriceObservation>
        Prices(params MarketPriceObservation[] prices) =>
            prices.ToDictionary(price => (price.DofusDbId, price.Quantity));
}

internal sealed class TestDataPriorityProvider : IDataPriorityProvider
{
    public DataPriority Priority { get; set; } = DataPriority.Manual;
}
