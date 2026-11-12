using BestCrush.Domain.Services;

namespace BestCrush.Services;

public sealed class CraftOpportunityProvider(MarketPriceService prices) : IOpportunityProvider
{
    public string Origin => "craft";
    public Task<IReadOnlyList<EconomicOpportunity>> EvaluateAsync(OpportunityScanContext context, CancellationToken cancellationToken)
    {
        List<EconomicOpportunity> result = [];
        foreach (var plan in context.Plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sale = prices.CalculateValue(plan.Product.DofusDbId, plan.Quantity, StockCraftPlanner.PricesFor(context.Market, plan.MarketType));
            string[] missing = (sale is null ? new[]{"Prix de revente manquant"} : Array.Empty<string>())
                .Concat(plan.Ingredients.Where(x=>!x.HasValuation).Select(x=>$"Prix d'ingrédient manquant : {x.Name}")).ToArray();
            result.Add(new(Origin,"Fabriquer puis revendre",context.Server,plan,sale?.Value,
                new(plan.Observations.Concat(sale?.UsedObservations ?? []).Distinct().ToArray()),missing)
            { UsesEstimatedPrices = sale?.IsEstimated == true || plan.Ingredients.Any(x=>x.OwnedValue?.IsEstimated==true || x.SurplusValue?.IsEstimated==true) });
        }
        return Task.FromResult<IReadOnlyList<EconomicOpportunity>>(result);
    }
}
