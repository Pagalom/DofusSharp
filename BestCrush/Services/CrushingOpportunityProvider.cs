using BestCrush.Domain.Models;
using BestCrush.Domain.Services;

namespace BestCrush.Services;

public sealed class CrushingOpportunityProvider(EquipmentProfitabilityService profitability,
    MarketPriceService prices, IBestCrushSettingsProvider settings) : IOpportunityProvider
{
    public string Origin => "concassage";
    public async Task<IReadOnlyList<EconomicOpportunity>> EvaluateAsync(OpportunityScanContext context, CancellationToken cancellationToken)
    {
        List<EconomicOpportunity> result = [];
        foreach (var plan in context.Plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (plan.Product is not Equipment equipment) continue;
            context.Market.Coefficients.TryGetValue(equipment.DofusDbId,out var coefficient);
            if (coefficient is null)
            {
                result.Add(new(Origin,"Fabriquer puis concasser",context.Server,plan,null,
                    new(plan.Observations.Distinct().ToArray()),["Coefficient de concassage local manquant"]));
                continue;
            }
            var calculation = await profitability.CalculateAsync(equipment,context.Server,context.Market,
                includeThresholds:false);
            EconomicOpportunity? best = null;
            foreach (var scenario in calculation.Scenarios)
            {
                var yields = scenario.Runes.Where(x=>x.Value>0).ToArray();
                var values = yields.Select(x=>prices.CalculateValue(x.Key.DofusDbId,x.Value*plan.Quantity,context.Market.RunePrices)).ToArray();
                // A partially priced focus must never outrank a fully valued strategy.
                if (yields.Length==0 || values.Any(x=>x is null)) continue;
                double revenue = values.Sum(x=>x!.Value.Value) * settings.CrushValueMultiplier;
                if (!double.IsFinite(revenue) || revenue<0) continue;
                var candidate = new EconomicOpportunity(Origin,"Fabriquer puis concasser",context.Server,plan,revenue,
                    new(plan.Observations.Concat(values.SelectMany(x=>x!.Value.UsedObservations)).Distinct().ToArray(),coefficient),
                    plan.Ingredients.Where(x=>!x.HasValuation).Select(x=>$"Prix d'ingrédient manquant : {x.Name}").ToArray(),
                    scenario.FocusedCharacteristic?.ToString() ?? "Sans focus",
                    yields.Select(x=>(x.Key.Name,x.Value*plan.Quantity)).ToArray())
                { UsesEstimatedPrices = values.Any(x=>x!.Value.IsEstimated) || plan.Ingredients.Any(x=>x.OwnedValue?.IsEstimated==true || x.SurplusValue?.IsEstimated==true) };
                if (best is null || candidate.Revenue>best.Revenue) best=candidate;
            }
            result.Add(best ?? new(Origin,"Fabriquer puis concasser",context.Server,plan,null,
                new(plan.Observations.Distinct().ToArray(),coefficient),["Prix de runes ou estimation de concassage incomplets"]));
        }
        return result;
    }
}
