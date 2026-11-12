using BestCrush.Domain.Models;

namespace BestCrush.Domain.Services;

public static class RuneMarketRanking
{
    public static double? Weight(Rune rune)
    {
        if (!Enum.IsDefined(rune.Characteristic)) return null;
        double baseWeight = rune.Characteristic.GetBasicRuneWeight();
        double statWeight = rune.Characteristic.GetWeight();
        if (!double.IsFinite(baseWeight) || !double.IsFinite(statWeight) || baseWeight <= 0 || statWeight <= 0)
            return null;
        return ForgemagingDataService.RuneGain(rune.Name, rune.Characteristic) * statWeight;
    }

    public static (int Lot, long Price)? Reference(long? price1, long? price10, long? price100, long? price1000)
    {
        (int Lot, long? Price)[] quotes = [(1, price1), (10, price10), (100, price100), (1000, price1000)];
        return quotes.Where(quote => quote.Price is > 0).OrderBy(quote => quote.Price!.Value / (double)quote.Lot)
            .Select(quote => ((int Lot, long Price)?)(quote.Lot, quote.Price!.Value)).FirstOrDefault();
    }
}
