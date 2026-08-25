using System.ComponentModel.DataAnnotations;

namespace BestCrush.Domain.Models;

public class MarketPriceObservation
{
    public Guid Id { get; private set; }

    public required MarketObjectType ObjectType { get; init; }

    public required long DofusDbId { get; init; }

    [MaxLength(64)]
    public required string ServerName { get; init; }

    public required long Price { get; init; }

    public int Quantity { get; init; } = 1;

    public required MarketPriceSource Source { get; init; }

    public required DateTime ObservedAtUtc { get; init; }
}