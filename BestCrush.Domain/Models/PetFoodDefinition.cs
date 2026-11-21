using System.ComponentModel.DataAnnotations;

namespace BestCrush.Domain.Models;

/// <summary>Shared food metadata, including runes and consumables outside Resources.</summary>
public sealed class PetFoodDefinition : IItem
{
    [Key] public long DofusDbId { get; set; }
    [MaxLength(256)] public string Name { get; set; } = "";
    public long? DofusDbIconId { get; set; }
    public int Level { get; set; }
    public MarketObjectType MarketType { get; set; }
    public double? Experience { get; set; }
    public DateTime? ExperienceObservedAtUtc { get; set; }
    public MarketPriceSource? ExperienceOrigin { get; set; }
    public bool Craftable { get; set; }
    [MaxLength(64)] public string ExperienceSource { get; set; } = "";
}
