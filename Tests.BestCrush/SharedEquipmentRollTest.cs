using System.Text.Json;
using BestCrush.Domain.Models;
using DofusSharp.DofusDb.ApiClients;
using DofusSharp.DofusDb.ApiClients.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using BestCrush.Domain.Services;
using BestCrush.Domain.Services.Upgrades;
using DofusSharp.DofusDb.ApiClients.Models.Items;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush;

public sealed class SharedEquipmentRollTest
{
    internal static readonly Dictionary<ulong,ForgemagingEffectDefinition> Definitions=new()
    {
        [112]=new(112,Characteristic.DamageFlatAll,1),
        [100]=new(100,null,1),
        [795]=new(795,null,1)
    };
    internal static byte[][] WeaponEffects()=>[
        Message(Unsigned(5,1),Unsigned(10,112)),
        Message(Bytes(8,Message(Unsigned(1,18),Unsigned(3,14))),Unsigned(10,100)),
        Message(Unsigned(5,0),Unsigned(10,795))];

    [Fact]
    public void KnownWeaponDamageDoesNotDiscardTheActualEquipmentRoll()
    {
        var raw=WeaponEffects();
        var wire=Message(Unsigned(3,1),Unsigned(4,30828),Unsigned(5,1658061),
            Message(raw.Select(effect=>Bytes(7,effect)).ToArray()));
        var fm=LiveForgemagingProtocol.Item(ProtoWire.ReadFields(wire))!;
        Assert.NotNull(fm);
        var expected=ForgemagedEquipmentStock.MapEffects(fm.Effects.Select(e=>(e.Code,e.Value)),Definitions)!;
        Assert.Equal(1,expected[Characteristic.DamageFlatAll]);
        Assert.Equal(1,expected[Characteristic.Hunting]);
        Assert.Equal(expected,ForgemagedEquipmentStock.MapWireEffects(raw,Definitions));

        var equipment=new Equipment(30828){Level=1};
        equipment.Characteristics=[new(equipment,Characteristic.DamageFlatAll,1,1),new(equipment,Characteristic.Hunting,1,1)];
        // Generic grouping remains conservative; the equipment reader uses catalogued effects.
        var stock=ForgemagedEquipmentStock.ReadStockRoll(equipment,new(30828,1658061,1,null,raw,"missing-value"),Definitions)!;
        Assert.Equal(1,stock.Single(line=>line.Characteristic==Characteristic.DamageFlatAll).Current);
        Assert.Equal(1,stock.Single(line=>line.Characteristic==Characteristic.Hunting).Current);
        var observed=new ItemDetailObservation(1658061,30828,1,[]){StatsComplete=false,EffectWire=raw};
        var crushed=CrushOperationAssessment.ResolveRoll(new(30828,23.41445,[]){ObservedItem=observed},equipment,null,Definitions,DateTime.UtcNow)!;
        Assert.Equal(1,crushed.Single(line=>line.Characteristic==Characteristic.DamageFlatAll).Current);
        Assert.Equal(1,crushed.Single(line=>line.Characteristic==Characteristic.Hunting).Current);
    }

    [Fact]
    public void HuntingLossIsTrackedAndIncludedInTheCrushingEstimate()
    {
        var before=ForgemagedEquipmentStock.MapWireEffects(WeaponEffects(),Definitions)!;
        var after=ForgemagedEquipmentStock.MapWireEffects(WeaponEffects()[..2],Definitions)!;
        Assert.Equal(1,before[Characteristic.Hunting]);
        Assert.False(after.ContainsKey(Characteristic.Hunting));
        Assert.Equal(5,Characteristic.Hunting.GetWeight());
        var session=new ForgemagingSession{ItemId=30828,Level=1,GameRecordingId="hunting-test",Lines=[
            new(){Characteristic=Characteristic.Hunting,Minimum=1,Maximum=1,Current=1,Target=1}]};
        var rune=new ForgemagingRune(10057,"Rune de chasse",Characteristic.Hunting,1,10);
        ForgemagingSessionActions.RecordObserved(session,rune,ForgemagingOutcome.Neutral,before,after,"hunting-loss",DateTime.UtcNow,5,2);
        Assert.Equal(0,session.Lines.Single(line=>line.Characteristic==Characteristic.Hunting).Current);
        Assert.Equal(5,session.Sink);
        var estimate=ForgemagingCrushAssessment.EstimateRoll(1,[new(){Characteristic=Characteristic.Hunting,Minimum=1,Maximum=1,Current=1}],
            [rune],new(100,DateTime.UtcNow),includeRuneQuantities:true);
        Assert.Equal(Characteristic.Hunting,Assert.Single(estimate.Lines).Stat);
        Assert.True(estimate.Lines[0].StandardQuantity>0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NativeHuntingRepairIsAdditiveAndRunsOnceForEachCatalogueVersion(bool raw)
    {
        using var database=new Utils.TestDatabase();
        using var db=database.CreateContext();
        var native=new Equipment(30828){Name="Bounihimée",Level=1};
        native.Characteristics.Add(new(native,Characteristic.DamageFlatAll,1,1));
        var exo=new Equipment(42){Name="Arme sans chasse native",Level=10};
        db.Equipments.AddRange(native,exo);
        await db.SaveChangesAsync();
        var originalId=native.Id;
        var table=Utils.CommonMocks.TableClient<DofusDbItem>();
        table.Setup(client=>client.SearchAsync(It.Is<DofusDbSearchQuery>(query=>
            query.Predicates.Contains(new DofusDbSearchPredicate.Eq("possibleEffects.baseEffectId","324"))),It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DofusDbSearchResult<DofusDbItem>{Total=1,Limit=50,Skip=0,Data=[new(){Id=30828,
                PossibleEffects=raw?[new(){BaseEffectId=324,Value=0,DiceNum=0,DiceSide=0}]:[],
                Effects=raw?[]:[new(){EffectId=795,Characteristic=0,From=0,To=0}]}]});
        var factory=new Mock<IDofusDbClientsFactory>();factory.Setup(client=>client.Items()).Returns(table.Object);
        var handler=new GameDataUpgradeHandler(db,Mock.Of<IDofusDbQueryProvider>(),Mock.Of<ILogger<GameDataUpgradeHandler>>());
        await handler.RepairNativeHuntingAsync(factory.Object,new(3,7,14),default);
        await handler.RepairNativeHuntingAsync(factory.Object,new(3,7,14),default);
        db.ChangeTracker.Clear();
        var repaired=await db.Equipments.Include(item=>item.Characteristics).SingleAsync(item=>item.DofusDbId==30828);
        Assert.Equal(originalId,repaired.Id);
        Assert.Equal(2,repaired.Characteristics.Count);
        Assert.Equal(1,repaired.Characteristics.Single(line=>line.Characteristic==Characteristic.Hunting).From);
        Assert.Equal(1,repaired.Characteristics.Single(line=>line.Characteristic==Characteristic.DamageFlatAll).From);
        Assert.Empty((await db.Equipments.Include(item=>item.Characteristics).SingleAsync(item=>item.DofusDbId==42)).Characteristics);
        table.Verify(client=>client.SearchAsync(It.IsAny<DofusDbSearchQuery>(),It.IsAny<CancellationToken>()),Times.Once);
    }

    [Theory]
    [InlineData(999999UL)] // Unknown effect cannot be discarded.
    [InlineData(112UL)] // A mapped stat still requires its numeric value.
    public void StructuredUnknownOrStatisticalEffectsDoNotCertifyACompleteRoll(ulong code)
    {
        var raw=WeaponEffects().Append(Message(Bytes(8,Unsigned(1,18)),Unsigned(10,code))).ToArray();
        Assert.Null(ForgemagedEquipmentStock.MapWireEffects(raw,Definitions));
        var at=DateTime.UtcNow;
        var session=new ForgemagingSession{LastObservedRollAtUtc=at,Lines=[new(){Characteristic=Characteristic.DamageFlatAll,Current=1}]};
        var observed=new ItemDetailObservation(1,30828,1,[]){StatsComplete=false,EffectWire=raw,ObservedAtUtc=at.AddSeconds(1)};
        Assert.Null(CrushOperationAssessment.ResolveRoll(new(30828,23,[]){ObservedItem=observed},null,session,Definitions,at.AddSeconds(2)));
    }

    [Fact]
    public void MalformedOrOversizedEffectsAreRejectedEvenWhenTheCodeIsNonStatistical()
    {
        Assert.Null(LiveForgemagingProtocol.ReadEffects([Message(Unsigned(5,1),Unsigned(5,2),Unsigned(10,100))]));
        Assert.Null(LiveForgemagingProtocol.ReadEffects([Message(Bytes(5,[1]),Unsigned(10,100))]));
        Assert.Null(LiveForgemagingProtocol.ReadEffects(Enumerable.Repeat(WeaponEffects()[0],65).ToArray()));
        Assert.Null(LiveForgemagingProtocol.ReadEffects([new byte[1025]]));
    }

    [Fact]
    public void RawEffectsAreNotSerializedIntoObservationHistory()
    {
        var observed=new ItemDetailObservation(1,30828,1,[]){EffectWire=WeaponEffects()};
        Assert.DoesNotContain("EffectWire",JsonSerializer.Serialize(observed));
    }
}
