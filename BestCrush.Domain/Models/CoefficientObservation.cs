using System.ComponentModel.DataAnnotations;

namespace BestCrush.Domain.Models;

public class CoefficientObservation
{
    public Guid Id { get; private set; }

    public required long DofusDbId { get; init; }

    [MaxLength(64)]
    public required string ServerName { get; init; }

    public required double CoefficientPercent { get; init; }

    public required CoefficientSource Source { get; init; }

    public required DateTime ObservedAtUtc { get; init; }
}