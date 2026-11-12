using System.Numerics;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;
using FluentAssertions;
using Tests.BestCrush.Utils;
using static Tests.BestCrush.Utils.EconomicTestData;

namespace Tests.BestCrush;

public sealed class CrushingOpportunityTest
{
    private sealed class Settings : IBestCrushSettingsProvider
    {
        public bool EquipmentCaptureEnabled=>false;
        public bool RuneCaptureEnabled=>false;
        public bool ResourceCaptureEnabled=>false;
        public bool CoefficientCaptureEnabled=>false;
        public CrushYieldEstimationMode CrushYieldEstimationMode=>CrushYieldEstimationMode.Average;
        public double TargetRoiPercent=>20;
        public double CrushValueDiscountPercent=>10;
    }
    [Fact]
    public async Task BatchUsesRuneLotsAndDiscountAndSkipsPartiallyPricedStrategies()
    {
        using TestDatabase database=new();using var db=database.CreateContext();
        db.Runes.AddRange(new Rune(10){Name="Rune Fo",Characteristic=Characteristic.Strength},
            new Rune(11){Name="Rune Ine",Characteristic=Characteristic.Intelligence});
        await db.SaveChangesAsync();
        var prices=new MarketPriceService(db,new TestDataPriorityProvider());
        var settings=new Settings();
        // Null ItemsService proves this batch never falls back to an API request.
        var profitability=new EquipmentProfitabilityService(null!,new CrushService(database),
            new CoefficientService(db,new TestDataPriorityProvider()),prices,new CraftCostService(prices),settings);
        Equipment equipment=new(100){Name="Objet",Level=100};
        equipment.Characteristics.Add(new(equipment,Characteristic.Strength,10,10));
        equipment.Characteristics.Add(new(equipment,Characteristic.Intelligence,10,10));
        equipment.Recipe.Add(new(equipment,new Resource(1){Name="Bois"},3));
        var market=new EquipmentProfitabilityContext
        {
            ResourcePrices=Prices(Price(1,1,10)),EquipmentPrices=Prices(),
            RunePrices=Prices(Price(10,1,10,type:MarketObjectType.Rune),Price(10,10,60,type:MarketObjectType.Rune)),
            Coefficients=new(){{100,new(){DofusDbId=100,ServerName=Server,CoefficientPercent=100,Source=CoefficientSource.InGameAutomatic,ObservedAtUtc=ObservedAt}}}
        };
        var plan=new StockCraftPlanner(prices).Calculate(equipment,2,new Dictionary<long,BigInteger>{{1,6}},market);
        var rows=await new CrushingOpportunityProvider(profitability,prices,settings).EvaluateAsync(new(Server,[plan],market),default);
        var row=rows.Should().ContainSingle().Which;
        row.IsComplete.Should().BeTrue();row.Focus.Should().Be(Characteristic.Strength.ToString());
        row.Runes!.Single().Quantity.Should().BeApproximately(48,0.000001);
        row.Revenue.Should().BeApproximately(288,0.000001);
        row.Benefit.Should().BeApproximately(228,0.000001);
        row.Evidence.Prices.Where(x=>x.ObjectType==MarketObjectType.Rune).Select(x=>x.Quantity).Should().BeEquivalentTo([1,10]);
    }
    [Fact]
    public async Task FastBatchKeepsExistingValuationButDoesNotSearchCoefficientThresholds()
    {
        using TestDatabase database=new();using var db=database.CreateContext();
        db.Runes.Add(new Rune(10){Name="Rune Fo",Characteristic=Characteristic.Strength});await db.SaveChangesAsync();
        var prices=new MarketPriceService(db,new TestDataPriorityProvider());
        var profitability=new EquipmentProfitabilityService(null!,new CrushService(database),
            new CoefficientService(db,new TestDataPriorityProvider()),prices,new CraftCostService(prices),new Settings());
        Equipment equipment=new(100){Level=100};
        equipment.Characteristics.Add(new(equipment,Characteristic.Strength,10,10));
        var market=new EquipmentProfitabilityContext
        {
            ResourcePrices=Prices(),EquipmentPrices=Prices(Price(100,1,100,type:MarketObjectType.Equipment)),
            RunePrices=Prices(Price(10,1,10,type:MarketObjectType.Rune)),
            Coefficients=new(){{100,new(){DofusDbId=100,ServerName=Server,CoefficientPercent=100,Source=CoefficientSource.Manual,ObservedAtUtc=ObservedAt}}}
        };
        var normal=await profitability.CalculateAsync(equipment,Server,market);
        var fast=await profitability.CalculateAsync(equipment,Server,market,includeThresholds:false);
        fast.Scenarios.Select(x=>x.AdjustedRuneValue).Should().Equal(normal.Scenarios.Select(x=>x.AdjustedRuneValue));
        fast.Scenarios.Should().OnlyContain(x=>x.PurchaseMinimumCoefficientPercent==null && x.CraftMinimumCoefficientPercent==null);
        normal.Scenarios.Should().OnlyContain(x=>x.PurchaseMinimumCoefficientPercent!=null);
    }
}
