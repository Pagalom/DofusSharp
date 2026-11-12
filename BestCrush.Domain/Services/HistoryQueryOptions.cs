using BestCrush.Domain.Models;

namespace BestCrush.Domain.Services;

public enum HistoryObservationSource { Manual, Game, DoFocus }
public enum CrushHistoryEntryKind { Coefficient, Operation }

/// <summary>Applied in SQL before sorting and pagination. The upper date bound is exclusive.</summary>
public sealed record HistoryQueryOptions
{
    public DateTime? FromUtc { get; init; }
    public DateTime? BeforeUtc { get; init; }
    public HistoryObservationSource? Source { get; init; }
    public int? LotQuantity { get; init; }
    public bool IncludeCleared { get; init; } = true;
    public ItemHistoryDataKind? ItemDataKind { get; init; }
    public IReadOnlyCollection<EquipmentType> EquipmentTypes { get; init; } = [];
    public CrushHistoryEntryKind? CrushKind { get; init; }
    public int Skip { get; init; }
}

public sealed class CrushHistoryEntry
{
    public Guid Id { get; init; }
    public DateTime ObservedAtUtc { get; init; }
    public required string Name { get; init; }
    public long? DofusDbId { get; init; }
    public CrushHistoryEntryKind Kind { get; init; }
    public double? Value { get; init; }
    public int? Quantity { get; init; }
    public CoefficientSource? Source { get; init; }
    public bool IsCleared { get; init; }
    public CrushHistorySession? Session { get; set; }
}
