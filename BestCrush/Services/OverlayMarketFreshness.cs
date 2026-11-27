using BestCrush.Domain.Models;
using BestCrush.Domain.Services;

namespace BestCrush.Services;

public sealed record OverlayMarketIndicator(string Key,string Title,string Icon,IReadOnlyList<OverlayPriceLine> Lines,bool Applicable=true)
{
    public DataFreshness? Freshness => !Applicable ? null : Lines.Count==0 ? DataFreshness.Stale
        : Lines.Max(line=>line.CurrentFreshness??DataFreshness.Stale);
}
public sealed record OverlayEquipmentIndicators(IReadOnlyList<OverlayMarketIndicator> Groups,bool Loaded=true);

/// <summary>Projects existing calculations only; never loads prices or recalculates profitability.</summary>
public static class OverlayMarketFreshness
{
    public static DateTime? Oldest(IEnumerable<MarketPriceObservation>? observations) =>
        observations?.Select(observation=>(DateTime?)observation.ObservedAtUtc).Min();
    public static IEnumerable<string> OutdatedNames(IEnumerable<OverlayPriceLine> lines) =>
        lines.Where(line=>!line.IsStatistic && line.NeedsUpdate).Select(line=>line.Name).Distinct(StringComparer.CurrentCultureIgnoreCase);
    public static OverlayPriceLine Ingredient(CraftResourceCostLine line) => new(line.ResourceName,line.RequiredQuantity,line.Cost,
        line.PurchasedQuantity is >0 ? line.Cost/(double)line.PurchasedQuantity : null,
        line.SurplusQuantity is >0 ? $"{line.PurchasedQuantity} achetés · {line.SurplusQuantity} conservés" : null,
        !line.HasPrice || line.Purchase?.Freshness!=DataFreshness.Fresh,line.DofusDbId){ObservedAtUtc=Oldest(line.Purchase?.UsedObservations)};
    public static OverlayPriceLine Rune(Rune rune,MarketValueResult? price) =>
        new(rune.Name,1,price?.Value,price?.Value,Warning:price?.Freshness!=DataFreshness.Fresh,ItemId:rune.DofusDbId)
        {ObservedAtUtc=Oldest(price?.UsedObservations)};
    public static OverlayEquipmentIndicators From(Equipment equipment,EquipmentProfitabilityResult? calculation)
    {
        var craft=calculation?.CraftCost.Resources.Select(Ingredient).ToArray()
            ?? equipment.Recipe.Select(r=>new OverlayPriceLine(r.Resource.Name,r.Count,null,Warning:true,ItemId:r.Resource.DofusDbId))
                .Concat(equipment.EquipmentRecipe.Select(r=>new OverlayPriceLine(r.IngredientEquipment.Name,r.Count,null,Warning:true,ItemId:r.IngredientEquipment.DofusDbId))).ToArray();
        var purchase=calculation?.EquipmentCost;
        OverlayPriceLine item=new(equipment.Name,1,purchase?.Price,purchase?.Price,Warning:purchase is null,ItemId:equipment.DofusDbId)
            {ObservedAtUtc=purchase?.ObservedAtUtc};
        var runes=calculation?.PotentialRunes.Select(rune=>Rune(rune,
            calculation.PotentialRuneUnitValues.TryGetValue(rune,out var value)?value:null)).ToArray()??[];
        var coefficient=calculation?.Coefficient;
        OverlayPriceLine coefficientLine=new(equipment.Name,null,null,Warning:coefficient is null,ItemId:equipment.DofusDbId)
            {ObservedAtUtc=coefficient?.ObservedAtUtc,DisplayValue=coefficient is null?"—":$"{coefficient.CoefficientPercent:N2} %"};
        return new([
            new("craft","Craft","bi-tools",craft,craft.Length>0),
            new("purchase","Achat","bi-cart",[item]),
            new("runes","Runes","bi-hexagon",runes,runes.Length>0),
            new("coefficient","Coefficient","bi-percent",[coefficientLine])],calculation is not null);
    }
    public static OverlayEquipmentIndicators FromMarket(Equipment equipment,IReadOnlyList<Rune> catalogue,
        EquipmentProfitabilityContext market,MarketPriceService pricing,CraftCostService craft)
    {
        var potential=equipment.Characteristics.Where(line=>((double)line.From+line.To)/2>0)
            .Select(line=>CrushService.GetBasicRune(catalogue,line.Characteristic)).OfType<Rune>()
            .DistinctBy(rune=>rune.DofusDbId).OrderBy(rune=>rune.Name).ToArray();
        var values=new Dictionary<Rune,MarketValueResult>();
        foreach(var rune in potential)
            if(pricing.CalculateValue(rune.DofusDbId,1,market.RunePrices) is {} value)values[rune]=value;
        var result=new EquipmentProfitabilityResult(equipment,market.Coefficients.GetValueOrDefault(equipment.DofusDbId),
            market.EquipmentPrices.GetValueOrDefault((equipment.DofusDbId,1)),
            craft.Calculate(equipment,market.ResourcePrices,market.EquipmentPrices),[],[])
            {PotentialRunes=potential,PotentialRuneUnitValues=values};
        return From(equipment,result);
    }
}
