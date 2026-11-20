using System.Security.Cryptography;
using System.Text;
using Rune = BestCrush.Domain.Models.Rune;
using System.Text.Json;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;
using DofusSharp.DofusDb.ApiClients;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.BestCrush.Utils;
using static Tests.BestCrush.Network.NetworkPayload;
using BestCrush.Network.Protocol;
using System.Net;
using System.Reflection;
using BestCrush.Components.Overlays;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;

namespace Tests.BestCrush;

public sealed class OverlayOwnedTargetTest
{
    private sealed class Capture : IComponentActivator
    {
        public OverlayRoot Root = null!;
        public IComponent CreateInstance(Type type)
        {
            var component=(IComponent)Activator.CreateInstance(type)!;
            if(component is OverlayRoot root)Root=root;
            return component;
        }
    }

    private static async Task VerifyPickerAsync(OverlayWorkspaceService data, ItemsService items)
    {
        var capture=new Capture();var host=new OverlayWindowsService(data,null!);
        ((Dictionary<string,OverlayWindowView>)typeof(OverlayWindowsService).GetField("_windows",BindingFlags.NonPublic|BindingFlags.Instance)!
            .GetValue(host)!).Add("profit",new(){Id="profit",Kind=WorkspaceOverlayKind.Profitability,Title="Rentabilité"});
        using var provider=new ServiceCollection().AddLogging().AddSingleton(data).AddSingleton(host).AddSingleton(items)
            .AddSingleton<IComponentActivator>(capture).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var rendered=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<OverlayRoot>(
            ParameterView.FromDictionary(new Dictionary<string,object?>{{"WindowId","profit"}})));
        Assert.Contains("class=\"roll-selector\"",await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        await renderer.Dispatcher.InvokeAsync(()=>(Task)typeof(OverlayRoot).GetMethod("ToggleRoll",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(capture.Root,null)!);
        var html=await renderer.Dispatcher.InvokeAsync(()=>WebUtility.HtmlDecode(rendered.ToHtmlString()));
        Assert.Contains("owned-picker",html);Assert.Contains("owned-preview-stat",html);Assert.Contains("bi-diamond-fill earth",html);
        Assert.DoesNotContain("Autre objet",html);
    }
    private sealed class Settings:IBestCrushSettingsProvider
    {
        public bool EquipmentCaptureEnabled=>true;public bool RuneCaptureEnabled=>true;
        public bool ResourceCaptureEnabled=>true;public bool CoefficientCaptureEnabled=>true;
        public CrushYieldEstimationMode CrushYieldEstimationMode=>CrushYieldEstimationMode.Average;
        public double TargetRoiPercent=>0;public double CrushValueDiscountPercent=>0;
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TooltipUsesExactOwnedRollAndPaidPriceWhileMarketFocusStillUsesAverage(bool automatic)
    {
        string folder=Path.Combine(Path.GetTempPath(),"overlay-owned-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        try
        {
            using var database=new TestDatabase();using var db=database.CreateContext();
            var at=DateTime.UtcNow;
            var item=new Equipment(100){Name="Objet test",Level=100,Type=EquipmentType.Ring};
            item.Characteristics.Add(new(item,Characteristic.Strength,1,11));
            db.Equipments.Add(item);db.Runes.Add(new Rune(10){Name="Rune Fo",Characteristic=Characteristic.Strength});
            db.CoefficientObservations.Add(new(){DofusDbId=100,ServerName="Kourial",CoefficientPercent=100,ObservedAtUtc=at,Source=CoefficientSource.InGameAutomatic});
            db.MarketPriceObservations.Add(new(){ObjectType=MarketObjectType.Equipment,DofusDbId=100,ServerName="Kourial",Price=899,Quantity=1,ObservedAtUtc=at,Source=MarketPriceSource.InGameAutomatic});
            db.MarketPriceObservations.Add(new(){ObjectType=MarketObjectType.Rune,DofusDbId=10,ServerName="Kourial",Price=10,Quantity=1,ObservedAtUtc=at,Source=MarketPriceSource.InGameAutomatic});
            await db.SaveChangesAsync();
            var clients=new CurrentDofusClientsState();var window=new DofusClientWindowKey(111,1111);
            clients.Update([new DofusWindowInfo(new IntPtr(111),"Test - Ecaflip - 3.7.14.18 - Release","Dofus",0,0,100,100){ProcessId=1111}],window);
            var attributions=new WindowServerAttributionState();attributions.Reset(3);
            attributions.ObserveGame(new("S→C","jzs",[],at,null,3,ConnectionId:1,CandidateWindow:window,SyntheticWindowForReplay:true),true);
            attributions.AssignPlayedServer(clients.SelectedClient!,"Kourial");
            var servers=new CurrentServerState(attributions,automatic);servers.SelectServer(automatic?"Other":"Kourial");
            var stocks=new LiveStockService(new LiveStockRepository(Path.Combine(folder,"stock.db")),clients);
            byte[] Stock(params (ulong Uid,ulong Force)[] copies)=>Message(Unsigned(4,1),Message(copies.Select(copy=>Bytes(2,Bytes(1,Message(
                Unsigned(3,1),Unsigned(4,100),Unsigned(5,copy.Uid),Bytes(7,Message(Unsigned(5,copy.Force),Unsigned(10,118))))))).ToArray()));
            void Observe(params (ulong Uid,ulong Force)[] copies)=>stocks.Observe(new("S→C","irl",Stock(copies),at,new("Kourial",1),3,ConnectionId:1,CandidateWindow:window,SyntheticWindowForReplay:true));
            Observe((101,18));
            var store=new ForgemagingSessionStore(Path.Combine(folder,"fm.json"));
            store.RecordAcquisition("Kourial",100,ForgemagedEquipmentStock.InstanceKey("Kourial",100,101),new(EquipmentAcquisitionKind.Purchase,897,at));
            var factory=DofusDbClient.CreateProductionFactory(new Uri("https://catalogue.example"),null);
            var cacheKey=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("catalogue-example-fm-effects-v2")));
            File.WriteAllText(Path.Combine(folder,cacheKey+".json"),JsonSerializer.Serialize(new{UpdatedUtc=at,Data=new ForgemagingEffectDefinition[]{new(118,Characteristic.Strength,1)}}));
            var catalog=new ActivityCatalogService(factory,Moq.Mock.Of<IHttpClientFactory>(),folder);
            var priority=new TestDataPriorityProvider();var prices=new MarketPriceService(db,priority);var coefficients=new CoefficientService(db,priority);
            var items=new ItemsService(db,null!,null!,null!,null!);var craft=new CraftCostService(prices);var settings=new Settings();
            using var provider=new ServiceCollection().AddSingleton(items).AddSingleton(prices).AddSingleton(coefficients)
                .AddSingleton(craft).AddSingleton<IBestCrushSettingsProvider>(settings).AddSingleton(catalog).AddSingleton(store)
                .AddSingleton(new ForgemagingDataService(db,prices,coefficients,settings))
                .AddSingleton(new CrushService(database)).AddSingleton<EquipmentProfitabilityService>().BuildServiceProvider();
            var data=new OverlayWorkspaceService(provider.GetRequiredService<IServiceScopeFactory>(),servers,clients,stocks,null!,new(),NullLogger<OverlayWorkspaceService>.Instance);
            await data.FocusTooltipAsync(item);
            var actual=data.Profitability!;
            Assert.Equal("Jet réel",actual.RollLabel);Assert.Equal(897,actual.Purchase);
            Assert.True(data.CanChooseInventoryRoll);
            Assert.Null(data.Error);var quantity=actual.Scenario(null)!.Runes.Values.Sum();
            await data.FocusAsync(item);
            Assert.Equal("Jet moyen",data.Profitability!.RollLabel);Assert.Equal(899,data.Profitability.Purchase);
            Assert.True(data.CanChooseInventoryRoll);
            Assert.True(quantity>data.Profitability.Scenario(null)!.Runes.Values.Sum());
            Observe((101,18),(102,3));
            await data.FocusTooltipAsync(item);
            Assert.Equal("Jet réel indisponible",data.Profitability!.RollLabel);
            Assert.Equal(2,data.Owned.Count);Assert.Empty(data.Profitability.Crushing!.Scenarios);
            servers.SelectServer("Other");
            await data.FocusTooltipAsync(item);
            Assert.Equal(automatic?"Jet réel indisponible":"Jet moyen",data.Profitability!.RollLabel);
            Assert.Equal(automatic?"Kourial":"Other",data.Profitability.Server);
            var bank=Message(Unsigned(1,1),Bytes(2,Bytes(1,Message(Unsigned(3,1),Unsigned(4,100),Unsigned(5,103),Bytes(7,Message(Unsigned(5,5),Unsigned(10,118)))))));
            stocks.Observe(new("S→C","irp",bank,at,new("Kourial",1),3,ConnectionId:1,CandidateWindow:window,SyntheticWindowForReplay:true));
            await data.LoadOwnedAsync(inventoryOnly:true);
            Assert.Equal(2,data.Owned.Count);Assert.All(data.Owned,row=>Assert.Equal("inventory",row.Item.Container));
            Assert.Equal("Kourial",data.OwnedServer);

            servers.SelectServer("Kourial");
            await data.FocusAsync(item,"Kourial");
            await data.LoadOwnedAsync(inventoryOnly:true,itemId:100);
            Assert.Equal(2,data.InventoryChoices(100).Count);
            await VerifyPickerAsync(data,items);
            // Even if an old picker contains bank or unrelated entries, the roll selector cannot offer them.
            var other=new Equipment(200){Name="Autre objet"};
            var unrelated=ForgemagedEquipmentStock.Project("other",other,"inventory",1,at,[],null,null,new(null,null));
            var bankCopy=ForgemagedEquipmentStock.Project(ForgemagedEquipmentStock.InstanceKey("Kourial",100,103),item,"bank",1,at,[],null,null,new(null,null));
            typeof(OverlayWorkspaceService).GetProperty(nameof(data.Owned))!.SetValue(data,
                data.Owned.Concat(new[]{new OverlayOwnedChoice(unrelated),new OverlayOwnedChoice(bankCopy)}).ToArray());
            Assert.Equal(2,data.InventoryChoices(100).Count);
            Assert.All(data.InventoryChoices(100),row=>{Assert.Equal(100,row.Item.Equipment.DofusDbId);Assert.Equal("inventory",row.Item.Container);});
            await data.FocusAsync(other,"Kourial");
            Assert.False(data.CanChooseInventoryRoll);Assert.Empty(data.InventoryChoices(200));
            var owned=data.InventoryChoices(100).Single(row=>row.Item.Key==ForgemagedEquipmentStock.InstanceKey("Kourial",100,101)).Item;
            await data.FocusOwnedAsync(owned,"Kourial");
            NetworkCrushResultLine Destroy(ulong uid)=>new(100,100,[]){ObservedItem=new(uid,100,1,[])};
            var lease=servers.GetWindowCaptureLease(window)!.Value;
            data.ObserveCrushedEquipment([Destroy(102)],lease,window);
            Assert.False(data.Profitability!.WasCrushed);
            data.ObserveCrushedEquipment([Destroy(101)],lease,new(222,2222));
            Assert.False(data.Profitability.WasCrushed);
            data.ObserveCrushedEquipment([Destroy(101)],new("Other",1),window);
            Assert.False(data.Profitability.WasCrushed);
            data.ObserveCrushedEquipment([Destroy(101)],lease,window);
            Assert.True(data.Profitability.WasCrushed);
            await data.RefreshAsync();Assert.True(data.Profitability!.WasCrushed);
            // Stock removal alone never marks another instance as crushed, and stale picker entries disappear.
            stocks.Observe(new("S→C","iqm",Unsigned(1,101),at,new("Kourial",1),3,ConnectionId:1,CandidateWindow:window,SyntheticWindowForReplay:true));
            Assert.Single(data.InventoryChoices(100));
            var second=data.InventoryChoices(100).Single().Item;
            await data.FocusOwnedAsync(second,"Kourial");Assert.False(data.Profitability!.WasCrushed);
            stocks.Observe(new("S→C","iqm",Unsigned(1,102),at,new("Kourial",1),3,ConnectionId:1,CandidateWindow:window,SyntheticWindowForReplay:true));
            Assert.False(data.CanChooseInventoryRoll);Assert.Empty(data.InventoryChoices(100));
            Assert.False(data.Profitability.WasCrushed);

        }
        finally{Directory.Delete(folder,true);}
    }
}
