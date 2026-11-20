using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;
using Tests.BestCrush.Utils;
using static Tests.BestCrush.Utils.EconomicTestData;

namespace Tests.BestCrush;

public sealed class OverlayProfitabilityTest
{
    private static Equipment Item()=>new(100){Name="Objet test",Level=100};
    private static readonly Rune Fo=new(10){Name="Rune Fo",Characteristic=Characteristic.Strength};
    private static readonly Rune Ine=new(11){Name="Rune Ine",Characteristic=Characteristic.Intelligence};
    private static EquipmentProfitabilityScenario Scenario(Equipment item,Characteristic? focus,double value,bool complete=true)
        =>new(item,new(){DofusDbId=100,ServerName=Server,CoefficientPercent=100,Source=CoefficientSource.InGameAutomatic,ObservedAtUtc=ObservedAt},
            null,new([]),null,null,null,null,focus,new Dictionary<Rune,double>{{Fo,10},{Ine,10}},
            complete?new Dictionary<Rune,MarketValueResult>{{Fo,new(value/2,false)},{Ine,new(value/2,false)}}:
                new Dictionary<Rune,MarketValueResult>{{Fo,new(value,false)}}){AdjustedRuneValue=value*.95};
    private static ProfitabilityOverlaySnapshot Snapshot(params (Characteristic? Focus,double Value,bool Complete)[] scenarios)
    {
        var item=Item();var values=scenarios.Select(s=>Scenario(item,s.Focus,s.Value,s.Complete)).ToArray();
        return new(item,Server,"Jet moyen",new(item,values.FirstOrDefault()?.Coefficient,null,new([]),values,[]),100,80,140,[],[],[]);
    }
    [Fact] public void DefaultCrushingFocusUsesMostValuableCompleteScenario()
    {
        var snapshot=Snapshot((null,100,true),(Characteristic.Strength,500,true),(Characteristic.Intelligence,900,false));
        var choice=new OverlayProfitabilitySelection();choice.Reset(snapshot,OverlayAcquisition.Craft);
        Assert.Equal(Characteristic.Strength,choice.Focus);Assert.Equal(OverlayRoute.CraftCrush,choice.Route);
        Assert.Equal(395,snapshot.Gain(choice.Route,choice.Focus));
    }
    [Fact] public void ManualFocusAndRoutePersistAcrossPricesButResetForAnotherAcquisition()
    {
        var first=Snapshot((null,100,true),(Characteristic.Strength,200,true));
        var choice=new OverlayProfitabilitySelection();choice.Reset(first,OverlayAcquisition.Craft);
        choice.SelectFocus(null);choice.SelectRoute(OverlayRoute.PurchaseSale);
        choice.Refresh(Snapshot((null,110,true),(Characteristic.Strength,400,true)));
        Assert.Null(choice.Focus);Assert.True(choice.ManualFocus);Assert.Equal(OverlayRoute.PurchaseSale,choice.Route);
        choice.Reset(first,OverlayAcquisition.Purchase);
        Assert.Equal(Characteristic.Strength,choice.Focus);Assert.Equal(OverlayRoute.PurchaseCrush,choice.Route);
    }
    [Fact] public void AutomaticallySelectedFocusTracksMarketChanges()
    {
        var choice=new OverlayProfitabilitySelection();choice.Reset(Snapshot((null,100,true),(Characteristic.Strength,200,true)),null);
        choice.Refresh(Snapshot((null,500,true),(Characteristic.Strength,200,true)));
        Assert.Null(choice.Focus);Assert.False(choice.ManualFocus);
    }
    [Fact] public void UnquotedRunesAndUnknownAcquisitionNeverProduceFalseProfit()
    {
        var snapshot=Snapshot((null,900,false));
        Assert.Null(snapshot.Revenue(OverlayRoute.PurchaseCrush,null));
        Assert.Null(snapshot.Gain(OverlayRoute.PurchaseCrush,null));
        var known=Snapshot((null,100,true)) with{Purchase=null};
        Assert.Null(known.Gain(OverlayRoute.PurchaseCrush,null));Assert.Null(known.Roi(OverlayRoute.PurchaseCrush,null));
    }
    [Fact] public void NetGainDeductsFmAndOriginCostsForBothSaleAndCrushing()
    {
        var snapshot=Snapshot((null,200,true)) with{FmCost=30};
        Assert.Equal(60,snapshot.Gain(OverlayRoute.PurchaseCrush,null));
        Assert.Equal(10,snapshot.Gain(OverlayRoute.PurchaseSale,null));
        Assert.Equal(100d*60/130,snapshot.Roi(OverlayRoute.PurchaseCrush,null));
        Assert.Null((snapshot with{FmCostKnown=false}).Gain(OverlayRoute.PurchaseCrush,null));
    }
    [Fact] public void NonConcassableProductKeepsSaleRoutesAvailable()
    {
        var snapshot=new ProfitabilityOverlaySnapshot(new Resource(1){Name="Alliage"},Server,"",null,100,80,140,[],[],[]);
        var choice=new OverlayProfitabilitySelection();choice.Reset(snapshot,null);
        Assert.Equal(OverlayRoute.CraftSale,choice.Route);Assert.Null(snapshot.Revenue(OverlayRoute.PurchaseCrush,null));
        Assert.Equal(60,snapshot.Gain(choice.Route,null));
    }
    private sealed class ConservativeSettings:IBestCrushSettingsProvider
    {
        public bool EquipmentCaptureEnabled=>false;public bool RuneCaptureEnabled=>false;
        public bool ResourceCaptureEnabled=>false;public bool CoefficientCaptureEnabled=>false;
        public CrushYieldEstimationMode CrushYieldEstimationMode=>CrushYieldEstimationMode.Conservative;
        public double TargetRoiPercent=>15;public double CrushValueDiscountPercent=>0;
    }
    [Fact] public async Task ExplicitAverageAndActualRollsOverridePessimisticSettingWithoutMutatingCatalogue()
    {
        using TestDatabase database=new();using var db=database.CreateContext();
        db.Runes.Add(new Rune(10){Name="Rune Fo",Characteristic=Characteristic.Strength});await db.SaveChangesAsync();
        var pricing=new MarketPriceService(db,new TestDataPriorityProvider());
        var calculator=new EquipmentProfitabilityService(null!,new CrushService(database),new CoefficientService(db,new TestDataPriorityProvider()),
            pricing,new CraftCostService(pricing),new ConservativeSettings());
        var item=Item();item.Characteristics.Add(new(item,Characteristic.Strength,1,11));
        var market=new EquipmentProfitabilityContext{ResourcePrices=Prices(),EquipmentPrices=Prices(Price(100,1,100,type:MarketObjectType.Equipment)),
            RunePrices=Prices(Price(10,1,10,type:MarketObjectType.Rune)),Coefficients=new(){{100,new(){DofusDbId=100,ServerName=Server,
                CoefficientPercent=100,Source=CoefficientSource.InGameAutomatic,ObservedAtUtc=ObservedAt}}}};
        var pessimistic=await calculator.CalculateAsync(item,Server,market,includeThresholds:false,allowRemoteCoefficient:false);
        var average=await calculator.CalculateAsync(item,Server,market,includeThresholds:false,allowRemoteCoefficient:false,
            observedLines:new Dictionary<Characteristic,double>{{Characteristic.Strength,6}});
        var actual=await calculator.CalculateAsync(item,Server,market,includeThresholds:false,allowRemoteCoefficient:false,
            observedLines:new Dictionary<Characteristic,double>{{Characteristic.Strength,18}});
        Assert.True(average.Scenarios[0].Runes.Values.Sum()>pessimistic.Scenarios[0].Runes.Values.Sum());
        Assert.True(actual.Scenarios[0].Runes.Values.Sum()>average.Scenarios[0].Runes.Values.Sum());
        Assert.Equal(1,item.Characteristics.Single().From);Assert.Equal(11,item.Characteristics.Single().To);
    }
}
