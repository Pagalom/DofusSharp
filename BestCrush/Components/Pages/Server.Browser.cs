using BestCrush.Components.Pure;
using BestCrush.Domain.Models;

namespace BestCrush.Components.Pages;

public partial class Server
{
    private string AllEquipmentUrl => $"/servers/{Uri.EscapeDataString(ServerName)}" + (_takeStocks ? "?stocks=true" : "");
    private bool MatchesSearchText(Equipment equipment) => string.IsNullOrWhiteSpace(Model?.SearchText) ||
        equipment.Name.Contains(Model.SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase);
    private CrushingResultSummary RankedCrushingSummary(ItemCrushResult item)
    {
        double gain = GetEffectiveBenefit(item);
        double? displayGain = double.IsFinite(gain) && gain != double.MinValue ? gain : null;
        return new(item.AdjustedRuneValue, displayGain, item.AdjustedRuneValue - displayGain);
    }
}
