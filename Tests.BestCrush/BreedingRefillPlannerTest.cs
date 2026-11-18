using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using DofusSharp.DofusDb.ApiClients.Models.Items;
using System.Numerics;
using System.Text.Json;
using Tests.BestCrush.Utils;

namespace Tests.BestCrush;

public sealed class BreedingRefillPlannerTest
{
    private static readonly BreedingFuelItem Small = new(10, "Petit extrait", 4_000, 80_000, PaddockGauge.Maturity);
    private static readonly BreedingFuelItem Big = new(20, "Grand extrait", 10_000, 140_000, PaddockGauge.Maturity);
    private static readonly ProductCraftRecipe Recipe = new(
        new(Big.Id, Big.Name, 15, null, MarketObjectType.Resource, 326, "Carburant", "Éleveur", 15),
        [new(30, "Ingrédient", MarketObjectType.Resource, 2, null)]);

    [Fact]
    public void ReadsDoseAndCapFromTheCurrentGameDataShape()
    {
        var item = JsonSerializer.Deserialize<DofusDbItem>("""
            {"id":33319,"name":{"fr":"Petit Extrait de Maturité"},
             "possibleEffects":[{"effectId":3816,"value":4000,"diceNum":100,"diceSide":800}]}
            """, JsonSerializerOptions.Web)!;
        Assert.Equal(new(33319, "Petit Extrait de Maturité", 4_000, 80_000, PaddockGauge.Maturity),
            ActivityCatalogService.ReadBreedingFuel(item));
    }

    [Theory]
    [InlineData(33320, 4000, 100, 800, 80_000)]
    [InlineData(33503, 10_000, 1, 0, 200_000)]
    public void UsesTheProductionGaugeWhenTheBetaEffectHasNoActionId(
        long id, int dose, int diceNum, int diceSide, int capacity)
    {
        var item = JsonSerializer.Deserialize<DofusDbItem>($$"""
            {"id":{{id}},"name":{"fr":"Carburant d’XP"},
             "possibleEffects":[{"baseEffectId":557,"value":{{dose}},"diceNum":{{diceNum}},"diceSide":{{diceSide}} }]}
            """, JsonSerializerOptions.Web)!;
        Assert.Null(ActivityCatalogService.ReadBreedingFuel(item));
        Assert.Equal(new(id, "Carburant d’XP", dose, capacity, PaddockGauge.Experience),
            ActivityCatalogService.ReadBreedingFuel(item, PaddockGauge.Experience));
    }

    [Fact]
    public void FinishedStockCanCoverTheSharedNeedAtZeroCost()
    {
        var plan = Plan(4_000, 7_000, new() { [Small.Id] = 1 }, null);
        Assert.NotNull(plan);
        Assert.Equal(0, plan.Cost);
        Assert.Equal(new(Small, 1), Assert.Single(plan.Stock));
        Assert.Empty(plan.Purchases);
        Assert.Equal(8_000, plan.ReserveAfter);
    }

    [Fact]
    public void BuysTheCheaperWholeLotEvenIfItContainsSurplus()
    {
        var plan = Plan(0, 16_000, new(), null, Quote(Small.Id, 1, 100), Quote(Small.Id, 10, 350));
        Assert.NotNull(plan);
        Assert.Equal(350, plan.Cost);
        Assert.Equal(10, Assert.Single(plan.Purchases).LotSize);
        Assert.Equal(16_000, plan.ReserveAfter);
    }

    [Fact]
    public void MixesFabricationAndMarketWhenTheJobLevelAllowsIt()
    {
        var stock = new Dictionary<long, BigInteger> { [30] = 1 };
        var quotes = new[] { Quote(Small.Id, 1, 100), Quote(Big.Id, 1, 240), Quote(30, 1, 60) };
        var plan = Plan(0, 14_000, stock, 15, quotes);
        Assert.NotNull(plan);
        Assert.Equal(160, plan.Cost);
        Assert.Equal(1, plan.Craft?.Quantity);
        Assert.Equal(60, Assert.Single(plan.Craft!.Ingredients).Purchase!.TotalCost);
        Assert.Equal(Small, Assert.Single(plan.Purchases).Item);

        var unqualified = Plan(0, 14_000, stock, 14, quotes);
        Assert.NotNull(unqualified);
        Assert.Null(unqualified.Craft);
        Assert.Equal(340, unqualified.Cost);
    }

    [Fact]
    public void UsesCheaperFuelBeforeCrossingItsCap()
    {
        var plan = Plan(76_000, 90_000, new(), null, Quote(Small.Id, 1, 30), Quote(Big.Id, 1, 150));
        Assert.NotNull(plan);
        Assert.Equal(180, plan.Cost);
        Assert.Equal(new[] { Small.Id, Big.Id }, plan.FuelingOrder.Select(use => use.Item.Id));
        Assert.Equal(90_000, plan.ReserveAfter);
    }

    [Fact]
    public void ExperienceCanUseCheapLowTierFuelAcrossSeveralRefills()
    {
        var plan = Plan(0, 88_000, new(), null, true,
            Quote(Small.Id, 10, 100), Quote(Big.Id, 10, 400));
        Assert.NotNull(plan);
        Assert.Equal(300, plan.Cost);
        Assert.All(plan.Purchases, purchase => Assert.Equal(Small.Id, purchase.Item.Id));
        Assert.Equal(TimeSpan.FromSeconds(88_000), plan.EstimateDuration(0, 88_000));
    }

    [Fact]
    public void DustFromStockCanCoverARefillWithoutKamas()
    {
        var plan = Plan(0, 10_000, new() { [BreedingRefillPlanner.DustItemId] = 200 }, null);
        Assert.NotNull(plan);
        Assert.Equal(0, plan.Cost);
        Assert.Equal(new(Big, 1, 200), Assert.Single(plan.Exchanges));
    }

    private static FuelRefillPlan? Plan(int reserve, int target, Dictionary<long, BigInteger> stock,
        int? level, params MarketPriceObservation[] quotes) => Plan(reserve, target, stock, level, false, quotes);

    private static FuelRefillPlan? Plan(int reserve, int target, Dictionary<long, BigInteger> stock,
        int? level, bool allowRefills, params MarketPriceObservation[] quotes)
    {
        var market = new EquipmentProfitabilityContext
        {
            ResourcePrices = quotes.ToDictionary(quote => (quote.DofusDbId, quote.Quantity)),
            EquipmentPrices = new Dictionary<(long, int), MarketPriceObservation>(),
            RunePrices = new Dictionary<(long, int), MarketPriceObservation>(),
            Coefficients = []
        };
        return BreedingRefillPlanner.Calculate(reserve, target, [Small, Big], stock, [Recipe],
            level, market, new(new(null!, new TestDataPriorityProvider())), allowRefills);
    }

    private static MarketPriceObservation Quote(long id, int quantity, int price) => new()
    {
        ObjectType = MarketObjectType.Resource,
        DofusDbId = id,
        ServerName = "Kourial",
        Quantity = quantity,
        Price = price,
        Source = default,
        ObservedAtUtc = DateTime.UtcNow
    };
}
