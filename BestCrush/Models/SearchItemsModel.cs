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
    public long? CraftJobId { get; set; }
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
    public bool RespectJobLevels { get; set; }
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

    public static IReadOnlyList<EquipmentType> SupportedTypes { get; } = Array.AsReadOnly(new[] {
        BestCrush.Domain.Models.EquipmentType.Amulet, BestCrush.Domain.Models.EquipmentType.Ring, BestCrush.Domain.Models.EquipmentType.Belt, BestCrush.Domain.Models.EquipmentType.Boots, BestCrush.Domain.Models.EquipmentType.Hat, BestCrush.Domain.Models.EquipmentType.Cloak, BestCrush.Domain.Models.EquipmentType.Shield, BestCrush.Domain.Models.EquipmentType.Trophy, BestCrush.Domain.Models.EquipmentType.Bow, BestCrush.Domain.Models.EquipmentType.Lance, BestCrush.Domain.Models.EquipmentType.Scythe, BestCrush.Domain.Models.EquipmentType.Axe, BestCrush.Domain.Models.EquipmentType.Tool, BestCrush.Domain.Models.EquipmentType.Pickaxe, BestCrush.Domain.Models.EquipmentType.Wand, BestCrush.Domain.Models.EquipmentType.Staff, BestCrush.Domain.Models.EquipmentType.Dagger, BestCrush.Domain.Models.EquipmentType.Sword, BestCrush.Domain.Models.EquipmentType.Hammer, BestCrush.Domain.Models.EquipmentType.Shovel });

    public EquipmentType[] GetSelectedTypes() => SupportedTypes.Where(type => type switch
    {
        BestCrush.Domain.Models.EquipmentType.Amulet => Amulet,
        BestCrush.Domain.Models.EquipmentType.Ring => Ring,
        BestCrush.Domain.Models.EquipmentType.Belt => Belt,
        BestCrush.Domain.Models.EquipmentType.Boots => Boots,
        BestCrush.Domain.Models.EquipmentType.Hat => Hat,
        BestCrush.Domain.Models.EquipmentType.Cloak => Cloak,
        BestCrush.Domain.Models.EquipmentType.Shield => Shield,
        BestCrush.Domain.Models.EquipmentType.Trophy => Trophy,
        BestCrush.Domain.Models.EquipmentType.Bow => Bow,
        BestCrush.Domain.Models.EquipmentType.Lance => Lance,
        BestCrush.Domain.Models.EquipmentType.Scythe => Scythe,
        BestCrush.Domain.Models.EquipmentType.Axe => Axe,
        BestCrush.Domain.Models.EquipmentType.Tool => Tool,
        BestCrush.Domain.Models.EquipmentType.Pickaxe => Pickaxe,
        BestCrush.Domain.Models.EquipmentType.Wand => Wand,
        BestCrush.Domain.Models.EquipmentType.Staff => Staff,
        BestCrush.Domain.Models.EquipmentType.Dagger => Dagger,
        BestCrush.Domain.Models.EquipmentType.Sword => Sword,
        BestCrush.Domain.Models.EquipmentType.Hammer => Hammer,
        BestCrush.Domain.Models.EquipmentType.Shovel => Shovel,
        _ => false
    }).ToArray();

    public void SetSelectedTypes(IEnumerable<EquipmentType> types)
    {
        var selected = types.ToHashSet();
        Amulet = selected.Contains(BestCrush.Domain.Models.EquipmentType.Amulet);
        Ring = selected.Contains(BestCrush.Domain.Models.EquipmentType.Ring);
        Belt = selected.Contains(BestCrush.Domain.Models.EquipmentType.Belt);
        Boots = selected.Contains(BestCrush.Domain.Models.EquipmentType.Boots);
        Hat = selected.Contains(BestCrush.Domain.Models.EquipmentType.Hat);
        Cloak = selected.Contains(BestCrush.Domain.Models.EquipmentType.Cloak);
        Shield = selected.Contains(BestCrush.Domain.Models.EquipmentType.Shield);
        Trophy = selected.Contains(BestCrush.Domain.Models.EquipmentType.Trophy);
        Bow = selected.Contains(BestCrush.Domain.Models.EquipmentType.Bow);
        Lance = selected.Contains(BestCrush.Domain.Models.EquipmentType.Lance);
        Scythe = selected.Contains(BestCrush.Domain.Models.EquipmentType.Scythe);
        Axe = selected.Contains(BestCrush.Domain.Models.EquipmentType.Axe);
        Tool = selected.Contains(BestCrush.Domain.Models.EquipmentType.Tool);
        Pickaxe = selected.Contains(BestCrush.Domain.Models.EquipmentType.Pickaxe);
        Wand = selected.Contains(BestCrush.Domain.Models.EquipmentType.Wand);
        Staff = selected.Contains(BestCrush.Domain.Models.EquipmentType.Staff);
        Dagger = selected.Contains(BestCrush.Domain.Models.EquipmentType.Dagger);
        Sword = selected.Contains(BestCrush.Domain.Models.EquipmentType.Sword);
        Hammer = selected.Contains(BestCrush.Domain.Models.EquipmentType.Hammer);
        Shovel = selected.Contains(BestCrush.Domain.Models.EquipmentType.Shovel);
    }
}
