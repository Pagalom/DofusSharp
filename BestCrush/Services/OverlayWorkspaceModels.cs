using BestCrush.Domain.Models;
using BestCrush.Domain.Services;

namespace BestCrush.Services;

public enum WorkspaceOverlayKind { Profitability, Crush, Forgemaging, Breeding, Pets, Journey, Control, Popup }
public enum OverlayAcquisition { Purchase, Craft }
public enum OverlayRoute { PurchaseSale, PurchaseCrush, CraftSale, CraftCrush }
public sealed record OverlayPriceLine(string Name, double? Quantity, double? Total, double? Unit = null,
    string? Detail = null, bool Warning = false, long? ItemId = null)
{
    public bool IsStatistic { get; init; }
    public Characteristic? Stat { get; init; }
}
public sealed record OverlayFact(string Label, string Value, string? Tone = null);
public sealed record OverlayActionCard(string Title, string? Subtitle, IReadOnlyList<OverlayFact> Facts,
    IReadOnlyList<OverlayPriceLine> Lines, string? Advice = null, double? Progress = null)
{
    public IReadOnlyList<OverlayGauge> Gauges { get; init; } = [];
    public string? Key { get; init; }
    public long ItemId { get; init; }
    public long? IconId { get; init; }
    public DateTime? Deadline { get; init; }
    public OverlayForgemagingTable? Forgemaging { get; init; }
}
public sealed record OverlayForgemagingTable(ForgemagingSession Session,IReadOnlyList<ForgemagingRune> Runes);
public sealed record OverlayGauge(string Name, string Icon, string Tone, int Fuel, int Capacity, bool Active);
public sealed record OverlayModuleView(string Title, IReadOnlyList<OverlayActionCard> Cards, string Empty,
    string ModuleSlug);
public sealed record OverlayPopup(string Key, string Title, IReadOnlyList<OverlayPriceLine> Lines,
    ProfitabilityOverlaySnapshot? Comparison = null)
{
    public DofusClientWindowKey? TargetWindow {get;init;}
    // Layouts belong to a popup family, never to an ephemeral item/focus GUID.
    internal OverlayLayoutKind LayoutKind => Comparison is not null ? OverlayLayoutKind.PopupRunes : Key.Split(':', 2)[0] switch
    {
        "craft" => OverlayLayoutKind.PopupCraft,
        "purchase" => OverlayLayoutKind.PopupPurchase,
        "checks" => OverlayLayoutKind.PopupPrices,
        _ => OverlayLayoutKind.PopupDetails
    };
}
public sealed record OverlayOwnedChoice(OwnedForgemagingEquipment Item);
public sealed record ProfitabilityOverlaySnapshot(IItem Item, string Server, string RollLabel,
    EquipmentProfitabilityResult? Crushing, double? Purchase, double? Craft, double? Sale,
    IReadOnlyList<OverlayPriceLine> Ingredients, IReadOnlyList<OverlayPriceLine> PurchaseChecks,
    IReadOnlyList<OverlayPriceLine> RuneChecks, double FmCost = 0, bool FmCostKnown = true)
{
    public Guid FocusId { get; init; }
    public bool WasCrushed { get; init; }
    public IReadOnlyList<OverlayPriceLine> PurchaseDetails { get; init; } = [];
    public bool ActualRollUnavailable => RollLabel == "Jet réel indisponible";
    public EquipmentProfitabilityScenario? BestFocus => Crushing?.Scenarios
        .Where(s => FullyPriced(s))
        .MaxBy(s => s.EstimatedRuneValue);
    public EquipmentProfitabilityScenario? Scenario(Characteristic? focus) =>
        Crushing?.Scenarios.FirstOrDefault(s => s.FocusedCharacteristic == focus);
    public static bool FullyPriced(EquipmentProfitabilityScenario scenario) => scenario.Runes.All(r=>
        r.Value<=0 || scenario.RuneValues.Keys.Any(key=>key.DofusDbId==r.Key.DofusDbId));
    public double? GrossRuneValue(Characteristic? focus) => Scenario(focus) is { } scenario && FullyPriced(scenario)
        ? scenario.EstimatedRuneValue : null;
    public double? Cost(OverlayRoute route) => route is OverlayRoute.CraftSale or OverlayRoute.CraftCrush ? Craft : Purchase;
    public double? Revenue(OverlayRoute route, Characteristic? focus) => IsCrushing(route)
        ? Scenario(focus) is { } scenario && FullyPriced(scenario)
            ? scenario.AdjustedRuneValue : null : Sale;
    public double? Gain(OverlayRoute route, Characteristic? focus) => Cost(route) is { } cost &&
        Revenue(route, focus) is { } revenue && FmCostKnown
            ? revenue - cost - FmCost : null;
    public double? Roi(OverlayRoute route, Characteristic? focus) =>
        Gain(route, focus) is { } gain && Cost(route) + FmCost is > 0 and var cost
            ? 100 * gain / cost : null;
    public static bool IsCrushing(OverlayRoute route) => route is OverlayRoute.PurchaseCrush or OverlayRoute.CraftCrush;
    public static string RouteName(OverlayRoute route) => route switch {
        OverlayRoute.PurchaseSale => "Achat → vente", OverlayRoute.PurchaseCrush => "Achat → concassage",
        OverlayRoute.CraftSale => "Craft → vente", _ => "Craft → concassage" };
}

/// <summary>Manual choices survive price updates, but never leak into a newly focused instance.</summary>
public sealed class OverlayProfitabilitySelection
{
    public OverlayRoute Route { get; private set; } = OverlayRoute.CraftCrush;
    public Characteristic? Focus { get; private set; }
    public bool ManualFocus { get; private set; }
    public void Reset(ProfitabilityOverlaySnapshot value, OverlayAcquisition? origin)
    {
        ManualFocus = false; Focus = value.BestFocus?.FocusedCharacteristic;
        Route = origin == OverlayAcquisition.Craft ? OverlayRoute.CraftCrush : origin == OverlayAcquisition.Purchase
            ? OverlayRoute.PurchaseCrush : value.Craft is not null && (value.Purchase is null || value.Craft < value.Purchase)
                ? OverlayRoute.CraftCrush : OverlayRoute.PurchaseCrush;
        if (value.Crushing is null) Route = value.Craft is not null ? OverlayRoute.CraftSale : OverlayRoute.PurchaseSale;
    }
    public void Refresh(ProfitabilityOverlaySnapshot value)
    {
        if (!ManualFocus) Focus = value.BestFocus?.FocusedCharacteristic;
        else if (value.Scenario(Focus) is null) { ManualFocus = false; Focus = value.BestFocus?.FocusedCharacteristic; }
    }
    public void SelectRoute(OverlayRoute route) => Route = route;
    public void SelectFocus(Characteristic? focus) { Focus = focus; ManualFocus = true; }
}
