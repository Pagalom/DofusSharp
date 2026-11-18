using System.Net;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using DofusSharp.DofusDb.ApiClients;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush;

public sealed class ForgemagingResistanceTrackingTest
{
    private static readonly (ulong Positive,ulong Negative,long Id,string Keyword,Characteristic Stat)[] Resistances=
    [
        (210,215,33,"earthElementResistPercent",Characteristic.ResistancePercentEarth),
        (211,216,35,"waterElementResistPercent",Characteristic.ResistancePercentWater),
        (212,217,36,"airElementResistPercent",Characteristic.ResistancePercentAir),
        (213,218,34,"fireElementResistPercent",Characteristic.ResistancePercentFire),
        (214,219,37,"neutralElementResistPercent",Characteristic.ResistancePercentNeutral),
        (240,245,54,"earthElementReduction",Characteristic.ResistanceFlatEarth),
        (241,246,56,"waterElementReduction",Characteristic.ResistanceFlatWater),
        (242,247,57,"airElementReduction",Characteristic.ResistanceFlatAir),
        (243,248,55,"fireElementReduction",Characteristic.ResistanceFlatFire),
        (244,249,58,"neutralElementReduction",Characteristic.ResistanceFlatNeutral)
    ];
    private static readonly (ulong Positive,ulong Negative,long Id,string Keyword,Characteristic Stat)[] Percentages=
    [
        (2800,2801,125,"dealtDamageMultiplierMelee",Characteristic.DamagePercentMelee),
        (2804,2805,120,"dealtDamageMultiplierDistance",Characteristic.DamagePercentDisance),
        (2808,2809,122,"dealtDamageMultiplierWeapon",Characteristic.DamagePercentWeapon),
        (2812,2813,123,"dealtDamageMultiplierSpells",Characteristic.DamagePercentSpell),
        (2803,2802,124,"receivedDamageMultiplierMelee",Characteristic.ResistancePercentMelee),
        (2807,2806,121,"receivedDamageMultiplierDistance",Characteristic.ResistancePercentDistance)
    ];
    private sealed class Reference:HttpMessageHandler,IHttpClientFactory
    {
        public int Requests;
        public HttpClient CreateClient(string name)=>new(this,false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Requests++;
            object[] data=request.RequestUri!.AbsolutePath.TrimEnd('/')=="/characteristics"
                ? Resistances.Concat(Percentages).Select(r=>(object)new{id=r.Id,keyword=r.Keyword}).Concat([
                    new{id=10,keyword="strength"},new{id=18,keyword="criticalHit"},new{id=25,keyword="damagePercent"}]).ToArray()
                : Resistances.Concat(Percentages).SelectMany(r=>new object[]{new{id=r.Positive,category=Resistances.Contains(r)?1:0,characteristic=r.Id,characteristicOperator="+"},
                    new{id=r.Negative,category=Resistances.Contains(r)?1:0,characteristic=r.Id,characteristicOperator="-"}}).Concat([
                    new{id=118UL,category=0,characteristic=10L,characteristicOperator="+"},
                    new{id=115UL,category=0,characteristic=18L,characteristicOperator="+"},
                    new{id=138UL,category=0,characteristic=25L,characteristicOperator="+"},
                    new{id=9991UL,category=2,characteristic=33L,characteristicOperator="+"},
                    new{id=9992UL,category=1,characteristic=33L,characteristicOperator="/"}]).ToArray();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{total=data.Length,limit=50,skip=0,data}),Encoding.UTF8,"application/json")});
        }
    }
    private static byte[] Item(ulong uid,long id,params (ulong Code,ulong Value)[] effects)=>Message(Unsigned(3,1),Unsigned(4,(ulong)id),Unsigned(5,uid),
        Message(effects.Select(e=>Bytes(7,Message(Unsigned(10,e.Code),Unsigned(5,e.Value)))).ToArray()));
    private static byte[] Workshop(byte[] item)=>Message([21,0,0,0,0],Bytes(3,Message(Bytes(1,item),Unsigned(4,63))));

    [Fact]
    public async Task EveryElementalResistanceCanBeReadAndForgemagedWithoutBreakingTracking()
    {
        string folder=Path.Combine(Path.GetTempPath(),"fm-resist-"+Guid.NewGuid());
        Directory.CreateDirectory(folder);
        using var http=new Reference();
        try
        {
            var catalog=new ActivityCatalogService(DofusDbClient.Production(),http,folder);
            var definitions=(await catalog.GetForgemagingEffectsAsync(default)).ToDictionary(e=>e.Code);
            foreach(var r in Resistances.Concat(Percentages))
            {
                Assert.Equal(r.Stat,definitions[r.Positive].Stat);Assert.Equal(r.Stat,definitions[r.Negative].Stat);
                Assert.Equal(-1,definitions[r.Negative].Sign);
                Assert.Equal(-2,ForgemagedEquipmentStock.MapEffects([(r.Negative,2L)],definitions)![r.Stat]);
                if(Percentages.Contains(r)) Assert.Equal(15,r.Stat.GetWeight());
                var initial=LiveForgemagingProtocol.Item(ProtoWire.ReadFields(Item(10,200,(r.Positive,3),(118,20))))!;
                var session=new ForgemagingSession{ItemId=200,Level=197,GameRecordingId="synthetic",Sink=0,
                    Lines=[new(){Characteristic=r.Stat,Current=3,Minimum=1,Maximum=10,Target=10},new(){Characteristic=Characteristic.Strength,Current=20}]};
                var rune=new ForgemagingRune(1000+(long)r.Positive,"Rune test",r.Stat,1,70);
                var tracker=new ForgemagingWorkshopTracker(session,initial,[rune],item=>ForgemagedEquipmentStock.MapEffects(item.Effects.Select(e=>(e.Code,e.Value)),definitions));
                var at=DateTime.UtcNow;
                tracker.Observe("C→S","jxu",Message(Unsigned(1,20),Unsigned(2,1)),at);
                Assert.Equal(ForgemagingTrackingUpdate.Ignored,tracker.Observe("S→C","kch",Workshop(Item(20,rune.Id,(r.Positive,1))),at));
                tracker.Observe("S→C","irx",Bytes(3,Message(Unsigned(1,9),Unsigned(3,20))),at);
                var result=Message(Unsigned(1,2),Bytes(2,Bytes(4,Item(10,200,(r.Positive,4),(118,20)))));
                Assert.Equal(ForgemagingTrackingUpdate.Recorded,tracker.Observe("S→C","kco",result,at));
                Assert.Equal(4,session.Lines.Single(l=>l.Characteristic==r.Stat).Current);
                Assert.Equal(20,session.Lines.Single(l=>l.Characteristic==Characteristic.Strength).Current);
                Assert.Equal(ForgemagingOutcome.Critical,Assert.Single(session.Attempts).Outcome);
            }
            Assert.Equal(Characteristic.Critical,definitions[115].Stat);
            Assert.Equal(Characteristic.Power,definitions[138].Stat);
            Assert.Null(definitions[9991].Stat);Assert.Null(definitions[9992].Stat);
        }
        finally{Directory.Delete(folder,true);}
    }

    [Fact]
    public void IgnoringResistanceMetadataReproducesTheInterruptionAtTheRuneDescription()
    {
        var definitions=new Dictionary<ulong,ForgemagingEffectDefinition>{{213,new(213,null,1)},{118,new(118,Characteristic.Strength,1)}};
        var initial=LiveForgemagingProtocol.Item(ProtoWire.ReadFields(Item(10,200,(213,6),(118,20))))!;
        var session=new ForgemagingSession{GameRecordingId="synthetic",ItemId=200,Level=197,Sink=0,
            Lines=[new(){Characteristic=Characteristic.ResistancePercentFire,Current=0,Target=7}]};
        var tracker=new ForgemagingWorkshopTracker(session,initial,[new(1000,"Rune Ré Per Feu",Characteristic.ResistancePercentFire,1,70)],
            item=>ForgemagedEquipmentStock.MapEffects(item.Effects.Select(e=>(e.Code,e.Value)),definitions));
        tracker.Observe("C→S","jxu",Message(Unsigned(1,20),Unsigned(2,1)),DateTime.UtcNow);
        Assert.Equal(ForgemagingTrackingUpdate.Gap,tracker.Observe("S→C","kch",Workshop(Item(20,1000,(213,1))),DateTime.UtcNow));
        Assert.Equal("rune-description-mismatch",tracker.LastGapReason);
        Assert.Empty(session.Attempts);Assert.Null(session.Lines[0].Current);
    }

    [Fact]
    public async Task ObsoleteCacheThatIgnoredResistancesIsNotReused()
    {
        string folder=Path.Combine(Path.GetTempPath(),"fm-resist-cache-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        using var http=new Reference();
        try
        {
            string key=DofusDbClient.ProductionUri.Host.Replace('.','-')+"-fm-effects-v1";
            string file=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))+".json";
            await File.WriteAllTextAsync(Path.Combine(folder,file),JsonSerializer.Serialize(new{UpdatedUtc=DateTime.UtcNow,
                Data=new ForgemagingEffectDefinition[]{new(213,null,1)}}));
            var catalog=new ActivityCatalogService(DofusDbClient.Production(),http,folder);
            Assert.Null(catalog.ReadCachedForgemagingEffects());
            var effects=await catalog.GetForgemagingEffectsAsync(default);
            Assert.Equal(Characteristic.ResistancePercentFire,Assert.Single(effects,e=>e.Code==213).Stat);
            Assert.True(http.Requests>0);
            var reopened=new ActivityCatalogService(DofusDbClient.Production(),Moq.Mock.Of<IHttpClientFactory>(),folder);
            Assert.Equal(Characteristic.ResistancePercentFire,reopened.ReadCachedForgemagingEffects()![213].Stat);
        }
        finally{Directory.Delete(folder,true);}
    }
}
