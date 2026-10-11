using BestCrush.Components.Pure;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Models;
using Microsoft.AspNetCore.Components;
using Rune = BestCrush.Domain.Models.Rune;

namespace BestCrush.Components.Pages;

public partial class History
{
    private static string FormatDate(DateTime utc) =>
        utc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

    private static string FormatMarketSource(MarketPriceSource source) =>
        source switch
        {
            MarketPriceSource.Manual => "Manuel",
            MarketPriceSource.InGameAutomatic => "Jeu",
            _ => source.ToString()
        };

    private static string FormatCoefficientSource(CoefficientSource source) =>
        source switch
        {
            CoefficientSource.Manual => "Manuel",
            CoefficientSource.InGameAutomatic => "Jeu",
            CoefficientSource.DofocusInitial => "DoFocus",
            _ => source.ToString()
        };

    private static string FormatDataKind(ItemHistoryDataKind kind) =>
        kind switch
        {
            ItemHistoryDataKind.Price => "Prix",
            ItemHistoryDataKind.Coefficient => "Coefficient",
            _ => kind.ToString()
        };

    private static string FormatItemSource(ItemHistoryRow row)
    {
        if (row.MarketSource is MarketPriceSource marketSource)
        {
            return FormatMarketSource(marketSource);
        }

        return row.CoefficientSource is CoefficientSource coefficientSource
            ? FormatCoefficientSource(coefficientSource)
            : "—";
    }

    private static string FormatItemValue(ItemHistoryRow row)
    {
        if (row.IsCleared)
        {
            return "—";
        }

        return row.DataKind == ItemHistoryDataKind.Price
            ? $"{row.Value:N0} K"
            : $"{row.Value:0.##} %";
    }

    private static string FormatKamas(double? value) =>
        value is double amount
            ? $"{amount:N0} K"
            : "Indisponible";

    // An estimated remainder counts individual runes, not whole lots.
    // Keep the historical snapshot unchanged; only format its lot line.
    internal static string FormatCrushHistoryLot(CrushHistoryRuneLot lot)
    {
        if (lot.IsEstimated && lot.LotQuantity > 1)
        {
            double estimatedValue =
                (double)lot.Count * lot.LotPrice / lot.LotQuantity;

            return $"{lot.Count:N0}×({lot.LotPrice:N0}/{lot.LotQuantity:N0})" +
                   $" = {estimatedValue:N0} K";
        }

        double lotValue = (double)lot.Count * lot.LotPrice;
        return $"{lot.Count:N0}×{lot.LotQuantity:N0} = {lotValue:N0} K";
    }
}
