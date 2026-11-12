using BestCrush.Domain.Models;
using BestCrush.Services;

namespace BestCrush.Components.Pages;

public partial class Server
{
    private string AllEquipmentUrl => $"/servers/{Uri.EscapeDataString(ServerName)}" + (_takeStocks ? "?stocks=true" : "");
    private bool MatchesSearchText(Equipment equipment) => string.IsNullOrWhiteSpace(Model?.SearchText) ||
        equipment.Name.Contains(Model.SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase);
    private string GainColor(ItemCrushResult item)
    {
        double gain = GetEffectiveBenefit(item);
        // Derive the cost from this exact displayed gain, not the best ROI of another scenario.
        return CrushingGainColor.For(gain, item.AdjustedRuneValue - gain);
    }
    private static string CrushMoney(double? amount) => amount is not { } value || !double.IsFinite(value) || value == double.MinValue
        ? "—" : $"{value:N0} K";
}
