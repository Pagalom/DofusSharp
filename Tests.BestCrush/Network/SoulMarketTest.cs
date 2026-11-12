using System.Net;
using System.Text;
using System.Text.Json;
using BestCrush.Domain.Services;
using BestCrush.Domain.Models;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using DofusSharp.DofusDb.ApiClients;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tests.BestCrush.Utils;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class SoulMarketTest
{
    private static byte[] Fixture(string boss) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"NetworkFixtures",$"soul-market-{boss}.bin"));
    private static byte[] Effect(ulong id, byte[] value) => Bytes(5,Message(value,Unsigned(10,id)));
    private static byte[] Row(ulong item=33713,ulong boss=173,ulong slots=4,ulong group=553,ulong price=72990,
        ulong price10=0,ulong offer=1,ulong category=327,ulong? groupSlots=null,bool duplicate=false) => Bytes(1,Message(
            Unsigned(1,item),Bytes(2,Packed(price,price10,0,0)),Unsigned(3,offer),Unsigned(4,category),
            Effect(1111,Unsigned(5,slots)),Effect(4057,Bytes(9,Message(Unsigned(1,groupSlots??slots),Unsigned(2,group)))),
            Effect(duplicate?4057UL:4058UL,Bytes(9,Message(Unsigned(1,5),Unsigned(4,boss))))));
    private static byte[] Body(byte[] row,ulong item=33713,ulong category=327)=>Message(row,Unsigned(2,category),Unsigned(3,item));

    [Fact]
    public void CapturedAncestralPreservesAllOffersForTheSameBoss()
    {
        var snapshot=Assert.IsType<SoulMarketObservation>(SoulMarketDecoder.Decode(Fixture("ancestral")));
        Assert.Equal(33713,snapshot.ItemId);Assert.Equal(2,snapshot.Offers.Count);
        var four=Assert.Single(snapshot.Offers,x=>x.Prices[0]==72990);
        Assert.Equal(173,four.BossId);Assert.Equal(553,four.GroupId);Assert.Equal(72990UL,four.Prices[0]);
        Assert.Contains(snapshot.Offers,x=>x.Prices[0]==135000 && x.BossId==173);
    }

    [Fact]
    public void CapturedBatofuPreservesLotTotalAndBossIdentity()
    {
        var snapshot=Assert.IsType<SoulMarketObservation>(SoulMarketDecoder.Decode(Fixture("batofu")));
        Assert.Equal(33685,snapshot.ItemId);var row=Assert.Single(snapshot.Offers);
        Assert.Equal(800,row.BossId);Assert.Equal(272,row.GroupId);
        Assert.Equal(new ulong[]{29996,250000,0,0},row.Prices);
    }

    [Fact]
    public void RejectsForeignCategoryMismatchedItemEffectsDuplicatesOverflowAndTruncation()
    {
        byte[][] invalid=[[],new byte[1024*1024+1],Fixture("ancestral")[..^1],
            Body(Row(item:12)),Body(Row(boss:0)),Body(Row(group:0)),
            Body(Row(duplicate:true)),Body(Row(price:ulong.MaxValue)),Body(Row(price:0)),Body(Row(),category:326),
            Body(Row(category:326)),Body(Message(Row(),Row())),Message(Unsigned(2,327),Unsigned(3,33713)),
            Message(Body(Row()),Unsigned(3,33713)),[0x0a,0xff,0xff,0xff,0xff,0x07]];
        foreach(var body in invalid)Assert.Null(SoulMarketDecoder.Decode(body));
        Assert.True(SoulMarketDecoder.IsBossSoulSelection(Message(Unsigned(2,327),Unsigned(3,33713))));
        Assert.False(SoulMarketDecoder.IsBossSoulSelection(Body(Row(),category:326)));
        Assert.NotNull(SoulMarketDecoder.Decode(Body(Row(slots:3,groupSlots:8))));
    }

    [Fact]
    public void PersistsAtomicSnapshotsWithLowestPricePerBossWithoutMixingServersOrLotsAndHonoursNewerManualQuote()
    {
        string folder=Path.Combine(Path.GetTempPath(),"soul-market-db-"+Guid.NewGuid());
        try
        {
            var repository=new GlobalSalesRepository(Path.Combine(folder,"sales.db"));var at=DateTimeOffset.UtcNow.AddMinutes(-5);
            int notifications=0;repository.Changed+=_=>notifications++;
            var ancestral=SoulMarketDecoder.Decode(Fixture("ancestral"))!;
            Assert.True(repository.SaveSoulMarket("Tylezia",ancestral,at));
            Assert.False(repository.SaveSoulMarket("Tylezia",ancestral,at));
            Assert.Equal(1,notifications);Assert.Empty(repository.ReadSoulQuotes("Kourial"));
            var four=Assert.Single(repository.ReadSoulQuotes("Tylezia"));Assert.Equal(72990d,four.Price);
            Assert.True(four.IsAutomatic);Assert.Equal(33713,four.ItemId);Assert.Equal(1,four.LotQuantity);
            repository.SaveSoulMarket("Tylezia",SoulMarketDecoder.Decode(Fixture("batofu"))!,at);
            var batofu=Assert.Single(repository.ReadSoulQuotes("Tylezia"),x=>x.BossId==800);
            Assert.Equal(29996d,batofu.Price); // x1 is the resale reference, not the cheaper x10 unit.
            var fallback=SoulMarketDecoder.Decode(Body(Row(price:0,price10:250001)))!;
            repository.SaveSoulMarket("Tylezia",fallback,at.AddSeconds(1));
            four=Assert.Single(repository.ReadSoulQuotes("Tylezia"),x=>x.BossId==173);
            Assert.Equal(25000.1d,four.Price,6);Assert.Equal(10,four.LotQuantity);Assert.Equal(250001,four.LotPrice);
            Assert.Equal(2,repository.ReadSoulQuotes("Tylezia").Count); // Latest snapshot replaces only this item.
            Assert.False(repository.SaveSoulMarket("Tylezia",ancestral,at));
            repository.SaveSoulQuote(new("Tylezia",173,70000,at.AddSeconds(2)));
            Assert.False(Assert.Single(repository.ReadSoulQuotes("Tylezia"),x=>x.BossId==173).IsAutomatic);
            repository.SaveSoulMarket("Tylezia",ancestral,at.AddSeconds(3));
            Assert.True(Assert.Single(repository.ReadSoulQuotes("Tylezia"),x=>x.BossId==173).IsAutomatic);
        }
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }

    private sealed class CatalogHttp(bool includeSoulItems = true, bool useBaseEffects = false):HttpMessageHandler,IHttpClientFactory
    {
        public HttpClient CreateClient(string name)=>new(this,false);
        private object Effect(int action, int baseId, int value, int diceNum = 0, int diceSide = 0) =>
            useBaseEffects ? new { baseEffectId = baseId, value, diceNum, diceSide } :
                new { effectId = action, value, diceNum, diceSide };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            string route=request.RequestUri!.AbsolutePath.Trim('/');string query=WebUtility.UrlDecode(request.RequestUri.Query);
            object[] data=route switch
            {
                "items" when query.Contains("typeId=327")=>includeSoulItems ? [
                    new{id=33713,typeId=327,possibleEffects=new[]{Effect(4058,593,173),Effect(4057,592,0,diceSide:553)}},
                    new{id=33685,typeId=327,possibleEffects=new[]{Effect(4058,593,800),Effect(4057,592,0,diceSide:272)}}] : [],
                "items"=>[new{id=9687,typeId=83,name=new{fr="Pierre"},possibleEffects=new[]{Effect(705,119,100,diceNum:100),Effect(1175,428,0)}}],
                "dungeons"=>[new{id=1,name=new{fr="Donjon"},bosses=new[]{173,800}}],
                "monsters"=>[new{id=173,name=new{fr="Ancestral"},isBoss=true,isMiniBoss=false,soulCaptureForbidden=false,grades=new[]{new{level=90}}},
                    new{id=800,name=new{fr="Batofu"},isBoss=true,isMiniBoss=false,soulCaptureForbidden=false,grades=new[]{new{level=40}}}],
                _=>throw new InvalidOperationException(route)
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(
                JsonSerializer.Serialize(new{total=data.Length,limit=50,skip=0,data}),Encoding.UTF8,"application/json")});
        }
    }

    [Fact]
    public async Task ProcessorRequiresServerDirectionActiveLeaseEnabledCaptureAndMatchingPublicBossAndGroup()
    {
        string folder=Path.Combine(Path.GetTempPath(),"soul-market-flow-"+Guid.NewGuid());
        using var database=new TestDatabase();using var http=new CatalogHttp();
        try
        {
            var repository=new GlobalSalesRepository(Path.Combine(folder,"sales.db"));
            var catalog=new ActivityCatalogService(DofusDbClient.Production(),http,Path.Combine(folder,"catalog"));
            var services=new ServiceCollection();services.AddScoped(_=>database.CreateContext());services.AddSingleton(repository);services.AddSingleton(catalog);
            using var provider=services.BuildServiceProvider();
            var state=new CurrentServerState();state.SelectServer("Tylezia");var lease=state.GetCaptureLease();
            var settings=new Mock<IBestCrushSettingsProvider>();settings.SetupGet(x=>x.ResourceCaptureEnabled).Returns(true);
            var debug=new NetworkDebugWriter(state,()=>false,5555);
            var writer=new NetworkObservationWriter(provider.GetRequiredService<IServiceScopeFactory>(),state,new LastNetworkEquipmentState(),
                settings.Object,new MarketDataChangeNotifier(),debug,(_,_,_,_)=>Task.CompletedTask,NullLogger<DofusNetworkCaptureService>.Instance);
            var processor=new DofusNetworkMessageProcessor(writer,debug,state);
            var map=ProtocolMap.Load(Path.Combine(AppContext.BaseDirectory,"NetworkFixtures","protocol-map.json"));
            Task Send(byte[] data,string direction="S→C",NetworkCaptureLease? scope=null)=>processor.ProcessMessageAsync(map,
                new(direction,map.MarketSelectionResponse!,data,DateTime.UtcNow,scope??lease,1,ConnectionId:1),default);
            await Send(Fixture("ancestral"),"C→S");Assert.Empty(repository.ReadSoulQuotes("Tylezia"));
            await Send(Body(Row(boss:800)));Assert.Empty(repository.ReadSoulQuotes("Tylezia"));
            await Send(Body(Row(group:999)));Assert.Empty(repository.ReadSoulQuotes("Tylezia"));
            settings.SetupGet(x=>x.ResourceCaptureEnabled).Returns(false);
            await Send(Fixture("ancestral"));Assert.Empty(repository.ReadSoulQuotes("Tylezia"));
            settings.SetupGet(x=>x.ResourceCaptureEnabled).Returns(true);
            await Send(Fixture("ancestral"));Assert.Equal(72990d,Assert.Single(repository.ReadSoulQuotes("Tylezia")).Price);
            await Send(Message(Unsigned(2,327),Unsigned(3,33713)));
            state.SelectServer("Kourial");await Send(Fixture("batofu"));
            Assert.DoesNotContain(repository.ReadSoulQuotes("Tylezia"),x=>x.BossId==800);Assert.Empty(repository.ReadSoulQuotes("Kourial"));
            lease=state.GetCaptureLease();await Send(Fixture("batofu"));
            Assert.Equal(29996d,Assert.Single(repository.ReadSoulQuotes("Kourial")).Price);
        }
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyStoneIsRecognizedBeforeOpeningSoulTabAndDisabledOrStaleCaptureCannotWrite(bool useBaseEffects)
    {
        using var database=new TestDatabase();using var http=new CatalogHttp(useBaseEffects:useBaseEffects);
        string folder=Path.Combine(Path.GetTempPath(),"empty-soul-flow-"+Guid.NewGuid());
        try
        {
            var services=new ServiceCollection();services.AddScoped(_=>database.CreateContext());
            services.AddScoped<MarketPriceService>();services.AddSingleton<IDataPriorityProvider>(new TestDataPriorityProvider());
            var catalog = new ActivityCatalogService(DofusDbClient.Production(),http,Path.Combine(folder,"catalog"));
            services.AddSingleton(catalog);
            using var provider=services.BuildServiceProvider();
            var state=new CurrentServerState();state.SelectServer("Tylezia");var lease=state.GetCaptureLease();
            var settings=new Mock<IBestCrushSettingsProvider>();settings.SetupGet(x=>x.ResourceCaptureEnabled).Returns(false);
            var debug=new NetworkDebugWriter(state,()=>false,5555);
            var writer=new NetworkObservationWriter(provider.GetRequiredService<IServiceScopeFactory>(),state,new LastNetworkEquipmentState(),
                settings.Object,new MarketDataChangeNotifier(),debug,(_,_,_,_)=>Task.CompletedTask,NullLogger<DofusNetworkCaptureService>.Instance);
            var market=new MarketObservation(9687,[new(1,9687,new ulong[]{1200,10000,0,0})]);
            await writer.PersistMarketAsync(market,DateTime.UtcNow,lease!.Value,default,requireStackable:true);
            using(var db=database.CreateContext()){Assert.Empty(db.Resources);Assert.Empty(db.MarketPriceObservations);}
            settings.SetupGet(x=>x.ResourceCaptureEnabled).Returns(true);
            await writer.PersistMarketAsync(market,DateTime.UtcNow,lease!.Value,default,requireStackable:true);
            using(var db=database.CreateContext())
            {
                Assert.Equal("Pierre",Assert.Single(db.Resources).Name);
                var prices=db.MarketPriceObservations.ToArray();Assert.Equal(2,prices.Length);
                Assert.Equal(1200,Assert.Single(prices,x=>x.Quantity==1).Price);
                Assert.Equal(10000,Assert.Single(prices,x=>x.Quantity==10).Price);
                Assert.All(prices,x=>{Assert.Equal("Tylezia",x.ServerName);Assert.Equal(MarketObjectType.Resource,x.ObjectType);});
            }
            var boss = Assert.Single(await catalog.GetBossesAsync(default), boss => boss.Id == 173);
            Assert.Equal(100, Assert.Single(boss.Stones).Capacity);
            state.SelectServer("Kourial");
            await writer.PersistMarketAsync(market,DateTime.UtcNow,lease!.Value,default,requireStackable:true);
            using(var db=database.CreateContext())Assert.DoesNotContain(db.MarketPriceObservations,x=>x.ServerName=="Kourial");
        }
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }

    [Theory]
    [InlineData(true,false)]
    [InlineData(true,true)]
    [InlineData(false,false)]
    public async Task MatchedBossHistoryUsesTheCatalogueOrAPreviousOfferOnTheSameServer(bool includeSoulItems, bool useBaseEffects)
    {
        using var database=new TestDatabase();using var http=new CatalogHttp(includeSoulItems, useBaseEffects);
        string folder=Path.Combine(Path.GetTempPath(),"soul-history-flow-"+Guid.NewGuid());
        try
        {
            var repository=new GlobalSalesRepository(Path.Combine(folder,"sales.db"));
            var services=new ServiceCollection();services.AddScoped(_=>database.CreateContext());services.AddSingleton(repository);
            services.AddSingleton(new ActivityCatalogService(DofusDbClient.Production(),http,Path.Combine(folder,"catalog")));
            using var provider=services.BuildServiceProvider();
            var state=new CurrentServerState();state.SelectServer("Tylezia");var lease=state.GetCaptureLease();
            var settings=new Mock<IBestCrushSettingsProvider>();settings.SetupGet(x=>x.ResourceCaptureEnabled).Returns(true);
            var debug=new NetworkDebugWriter(state,()=>false,5555);
            var writer=new NetworkObservationWriter(provider.GetRequiredService<IServiceScopeFactory>(),state,new LastNetworkEquipmentState(),
                settings.Object,new MarketDataChangeNotifier(),debug,(_,_,_,_)=>Task.CompletedTask,NullLogger<DofusNetworkCaptureService>.Instance);
            var processor=new DofusNetworkMessageProcessor(writer,debug,state);
            var map=ProtocolMap.Load(Path.Combine(AppContext.BaseDirectory,"NetworkFixtures","protocol-map.json"));
            var now=DateTime.UtcNow;
            var point=Message(Unsigned(1,70000),Unsigned(2,33713),Bytes(3,Encoding.UTF8.GetBytes(now.AddMinutes(-1).ToString("O"))),Unsigned(4,7));
            Task Send(string direction,string key,byte[] body,long connection=1)=>processor.ProcessMessageAsync(map,
                new(direction,key,body,now,lease,1,ConnectionId:connection),default);
            await Send("S→C","ire",Bytes(3,point));Assert.Empty(repository.ReadHistories("Tylezia"));
            await Send("C→S","iqp",Unsigned(1,33713));await Send("S→C","ire",Bytes(3,point),2);
            Assert.Empty(repository.ReadHistories("Tylezia"));
            if (!includeSoulItems)
            {
                await Send("S→C", map.MarketSelectionResponse!, Body(Row(boss: 999)));
                await Send("S→C", map.MarketSelectionResponse!, Body(Message(Row(), Row(group: 999, offer: 2))));
                Assert.Empty(repository.ReadSoulQuotes("Tylezia"));

                await Send("S→C", map.MarketSelectionResponse!, Body(Row(price: 49997)));
                Assert.Equal(49997d, Assert.Single(repository.ReadSoulQuotes("Tylezia")).Price);
            }
            await Send("C→S","iqp",Unsigned(1,33713));await Send("S→C","ire",Bytes(3,point));
            var history=Assert.Single(repository.ReadHistories("Tylezia"));
            Assert.Equal(33713,history.Market.ItemId);Assert.Equal(GlobalSalesRepository.BossHistoryVariant(173),history.Market.Variant);
            Assert.Equal(7,history.Period(7).UnitsSold);
            if (includeSoulItems) Assert.Empty(repository.ReadSoulQuotes("Tylezia"));
            using(var db=database.CreateContext()){Assert.Empty(db.Resources);Assert.Empty(db.MarketPriceObservations);}
            state.SelectServer("Kourial");
            await Send("C→S","iqp",Unsigned(1,33713));await Send("S→C","ire",Bytes(3,point));
            Assert.Empty(repository.ReadHistories("Kourial"));
            if (!includeSoulItems)
            {
                lease=state.GetCaptureLease();
                await Send("C→S","iqp",Unsigned(1,33713));await Send("S→C","ire",Bytes(3,point));
                Assert.Empty(repository.ReadHistories("Kourial"));
            }
        }
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
}
