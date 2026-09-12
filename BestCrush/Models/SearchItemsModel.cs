using BestCrush.Domain.Models;

namespace BestCrush.Models;

public enum RuneFilterMatchMode
{
    Any,
    All
}

public enum ProfitabilityFilterMode
{
    Best,
    Purchase,
    Craft
}

public sealed class RuneFilterCriterion
{
    public long DofusDbId { get; set; }
    public Characteristic Characteristic { get; set; }
    public string RuneName { get; set; } = string.Empty;
}

public class SearchItemsModel
{
    public string? SearchText { get; set; }
    public int? LevelMin { get; set; }
    public int? LevelMax { get; set; }
    public EquipmentTypesFilter EquipmentType { get; set; } = new();

    public RuneFilterMatchMode RuneMatchMode { get; set; } =
        RuneFilterMatchMode.Any;

    public List<RuneFilterCriterion> RuneFilters { get; set; } = [];

    public ProfitabilityFilterMode ProfitabilityMode { get; set; } =
        ProfitabilityFilterMode.Best;

    public double? MinimumRoiPercent { get; set; }
    public double? MinimumBenefit { get; set; }
    public bool CompleteDataOnly { get; set; }
    public bool FreshDataOnly { get; set; }
    public bool RequireCoefficient { get; set; }
}

public sealed class SearchFilterPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public SearchItemsModel Filters { get; set; } = new();
    public SortOrder SortOrder { get; set; } = SortOrder.BestBenefit;
}

public class EquipmentTypesFilter
{
    public bool Amulet { get; set; }
    public bool Ring { get; set; }
    public bool Belt { get; set; }
    public bool Boots { get; set; }
    public bool Hat { get; set; }
    public bool Cloak { get; set; }
    public bool Shield { get; set; }
    public bool Trophy { get; set; }

    public bool Bow { get; set; }
    public bool Lance { get; set; }
    public bool Scythe { get; set; }
    public bool Axe { get; set; }
    public bool Tool { get; set; }
    public bool Pickaxe { get; set; }
    public bool Wand { get; set; }
    public bool Staff { get; set; }
    public bool Dagger { get; set; }
    public bool Sword { get; set; }
    public bool Hammer { get; set; }
    public bool Shovel { get; set; }
}
