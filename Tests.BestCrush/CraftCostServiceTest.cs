using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using FluentAssertions;
using Tests.BestCrush.Utils;
using static Tests.BestCrush.Utils.EconomicTestData;

namespace Tests.BestCrush;

public sealed class CraftCostServiceTest : IDisposable
{
    // The exercised Calculate overload only uses supplied observations, so no database is opened.
    private readonly BestCrushDbContext _context = new(new Microsoft.EntityFrameworkCore.DbContextOptions<BestCrushDbContext>());
    private readonly CraftCostService _service;

    public CraftCostServiceTest() =>
        _service = new CraftCostService(new MarketPriceService(_context, new TestDataPriorityProvider()));

    public void Dispose() => _context.Dispose();

    [Fact]
    public void GroupsResourceIngredientsBeforeBuyingLotsAndSortsLinesByName()
    {
        Equipment equipment = new(100);
        Resource ore = new(1) { Name = "Zinc" };
        Resource wood = new(2) { Name = "Ash" };
        equipment.Recipe.Add(new RecipeEntry(equipment, ore, 3));
        equipment.Recipe.Add(new RecipeEntry(equipment, new Resource(1) { Name = "Zinc" }, 6));
        equipment.Recipe.Add(new RecipeEntry(equipment, wood, 2));
        MarketPriceObservation oreLot = Price(1, 10, 70);

        CraftCostResult result = _service.Calculate(equipment, Prices(Price(1, 1, 100), oreLot, Price(2, 1, 5)), Prices());

        result.IsComplete.Should().BeTrue();
        result.TotalCost.Should().Be(80);
        result.KnownCost.Should().Be(80);
        result.MissingIngredientCount.Should().Be(0);
        result.Resources.Select(line => line.ResourceName).Should().Equal("Ash", "Zinc");
        CraftResourceCostLine zinc = result.Resources[1];
        zinc.ObjectType.Should().Be(MarketObjectType.Resource);
        zinc.RequiredQuantity.Should().Be(9);
        zinc.Cost.Should().Be(70);
        zinc.PurchasedQuantity.Should().Be(10);
        zinc.SurplusQuantity.Should().Be(1);
        zinc.Purchase!.Lots.Should().BeEquivalentTo(new Dictionary<int, int> { [10] = 1 });
        zinc.Purchase.UsedObservations.Should().ContainSingle().Which.Should().BeSameAs(oreLot);
    }

    [Fact]
    public void EquipmentIngredientsUseSinglePurchasePriceWithoutRecursingIntoTheirRecipeOrBuyingBulk()
    {
        Equipment equipment = new(100);
        Equipment ingredient = new(2) { Name = "Ingredient equipment" };
        ingredient.Recipe.Add(new RecipeEntry(ingredient, new Resource(3) { Name = "Cheap resource" }, 1));
        equipment.EquipmentRecipe.Add(new EquipmentRecipeEntry(equipment, ingredient, 1));
        equipment.EquipmentRecipe.Add(new EquipmentRecipeEntry(equipment, ingredient, 2));
        MarketPriceObservation single = Price(2, 1, 50, type: MarketObjectType.Equipment);

        CraftCostResult result = _service.Calculate(equipment, Prices(Price(3, 1, 1)),
            Prices(single, Price(2, 10, 1, type: MarketObjectType.Equipment)));

        result.TotalCost.Should().Be(150);
        CraftResourceCostLine line = result.Resources.Should().ContainSingle().Which;
        line.ObjectType.Should().Be(MarketObjectType.Equipment);
        line.RequiredQuantity.Should().Be(3);
        line.PurchasedQuantity.Should().Be(3);
        line.SurplusQuantity.Should().Be(0);
        line.Purchase!.Lots.Should().BeEquivalentTo(new Dictionary<int, int> { [1] = 3 });
        line.Purchase.UsedObservations.Should().ContainSingle().Which.Should().BeSameAs(single);
    }

    [Fact]
    public void MissingResourceAndMissingEquipmentSinglePriceKeepKnownCostButNoTotal()
    {
        Equipment equipment = new(100);
        equipment.Recipe.Add(new RecipeEntry(equipment, new Resource(1) { Name = "Known resource" }, 2));
        equipment.Recipe.Add(new RecipeEntry(equipment, new Resource(2) { Name = "Missing resource" }, 1));
        equipment.EquipmentRecipe.Add(new EquipmentRecipeEntry(equipment, new Equipment(3) { Name = "Missing equipment" }, 1));

        CraftCostResult result = _service.Calculate(equipment,
            Prices(Price(1, 1, 4), Price(3, 1, 1)),
            Prices(Price(3, 10, 20, type: MarketObjectType.Equipment)));

        result.IsComplete.Should().BeFalse();
        result.TotalCost.Should().BeNull();
        result.KnownCost.Should().Be(8);
        result.MissingIngredientCount.Should().Be(2);
        result.MissingResourceCount.Should().Be(2);
        result.Resources.Should().HaveCount(3);
        result.Resources.Where(line => !line.HasPrice).Select(line => line.DofusDbId).Should().BeEquivalentTo(new long[] { 2, 3 });
        result.Resources.Where(line => !line.HasPrice).Should().OnlyContain(line => line.Purchase == null && line.Cost == null);
    }

    [Fact]
    public void EmptyRecipeIsCompleteWithZeroCost()
    {
        CraftCostResult result = _service.Calculate(new Equipment(100), Prices(), Prices());

        result.IsComplete.Should().BeTrue();
        result.TotalCost.Should().Be(0);
        result.KnownCost.Should().Be(0);
        result.MissingIngredientCount.Should().Be(0);
        result.Resources.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void NonPositiveEquipmentPurchasePriceIsMissing(long price)
    {
        Equipment equipment = new(100);
        equipment.EquipmentRecipe.Add(new EquipmentRecipeEntry(equipment, new Equipment(2), 1));

        CraftCostResult result = _service.Calculate(equipment, Prices(), Prices(Price(2, 1, price, type: MarketObjectType.Equipment)));

        result.IsComplete.Should().BeFalse();
        result.TotalCost.Should().BeNull();
        result.Resources.Should().ContainSingle().Which.HasPrice.Should().BeFalse();
    }

    [Fact]
    public void EquipmentPurchaseMultiplicationIsCheckedForOverflow()
    {
        Equipment equipment = new(100);
        equipment.EquipmentRecipe.Add(new EquipmentRecipeEntry(equipment, new Equipment(2), 2));

        Assert.Throws<OverflowException>(() => _service.Calculate(equipment, Prices(), Prices(Price(2, 1, long.MaxValue, type: MarketObjectType.Equipment))));
    }
}
