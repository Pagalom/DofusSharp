using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BestCrush.Domain.Models;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using DofusSharp.DofusDb.ApiClients;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.BestCrush.Utils;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush;

public sealed class PetOpportunityServiceTest
{
    [Fact]
    public async Task CompleteLiveInventoryAndBankFeedThePlanButArchivesAndOtherServersDoNot()
    {
        string folder=Path.Combine(Path.GetTempPath(),"pet-service-"+Guid.NewGuid());
        Directory.CreateDirectory(folder);
        using var database=new TestDatabase();
        try
        {
            void Cache(string key,object data)
            {
                string host=DofusDbClient.ProductionUri.Host.Replace('.','-');
                string file=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(host+"-"+key)))+".json";
                File.WriteAllText(Path.Combine(folder,file),JsonSerializer.Serialize(new{UpdatedUtc=DateTime.UtcNow,Data=data}));
            }
            Cache("pet-species-v1",new[]{new PetSpecies(100,"Familier test",null,18)});
            var catalog=new ActivityCatalogService(DofusDbClient.Production(),Moq.Mock.Of<IHttpClientFactory>(),folder);
            var window=new DofusClientWindowKey(111,1111);
            var clients=new CurrentDofusClientsState();
            clients.Update([new DofusWindowInfo(new IntPtr(window.Handle),"Test - Ecaflip - 3.7.14.18 - Release","Dofus",0,0,100,100){ProcessId=window.ProcessId}],window);
            using var stock=new LiveStockService(new LiveStockRepository(Path.Combine(folder,"stock.db")),clients);
            var at=DateTime.UtcNow;
            byte[] Item(ulong id,ulong uid,ulong quantity,params byte[][] effects)=>Message(Unsigned(3,quantity),Unsigned(4,id),Unsigned(5,uid),
                Message(effects.Select(effect=>Bytes(7,effect)).ToArray()));
            byte[] Snapshot(string opcode,params byte[][] items)=>Message(Unsigned(opcode=="irp"?1:4,1),Message(items.Select(item=>Bytes(2,Bytes(1,item))).ToArray()));
            void Observe(string opcode,byte[] body)=>stock.Observe(new("S→C",opcode,body,at,new NetworkCaptureLease("A",1),1,
                ConnectionId:1,CandidateWindow:window,SyntheticWindowForReplay:true));
            Observe("irl",Snapshot("irl",Item(100,1,1,Message(Unsigned(10,2821),Bytes(9,Unsigned(1,18))),
                Message(Unsigned(10,2819),Bytes(9,Unsigned(2,1)))),Item(1796,2,1)));
            Observe("irp",Snapshot("irp",Item(1796,3,2)));
            using(var db=database.CreateContext())
            {
                db.MarketPriceObservations.Add(new(){ObjectType=MarketObjectType.Resource,DofusDbId=1796,ServerName="A",
                    Price=2,Quantity=1,Source=MarketPriceSource.InGameAutomatic,ObservedAtUtc=at});
                await db.SaveChangesAsync();
            }
            var sales=new GlobalSalesRepository(Path.Combine(folder,"sales.db"));
            sales.SavePetPrices("A",new(100,[new(0,0,100,"normal",false),new(23,90,500,"normal",false)]),at);
            var server=new CurrentServerState();
            var foodPreferences=new PetFoodPreferences();
            var service=new PetOpportunityService(database,new TestDataPriorityProvider(),catalog,stock,server,
                new CurrentWindowProfessionsState(server),sales,NullLogger<PetOpportunityService>.Instance,foodPreferences:foodPreferences);
            var report=(await service.LoadAsync(window,"A",default))!;
            Assert.True(report.InventoryKnown);Assert.True(report.BankKnown);
            var pet=Assert.Single(report.Owned);
            Assert.Equal(0,pet.Owned!.Reading!.Level);
            Assert.Equal(23,pet.Best!.Level);
            Assert.Equal(3,pet.Best.Nutrition.StockConsumed[1796]);
            Assert.Equal(12,pet.Best.Nutrition.CashRequired);
            Assert.Equal(3,pet.Best.Nutrition.StockValue);
            Assert.Equal(385,pet.Best.Margin); // 499 - 99 - 12 - 3.
            foodPreferences.SetUseStocks(false);
            var withoutStock=(await service.LoadAsync(window,"A",default))!;
            var purchasedFood=Assert.Single(withoutStock.Owned).Best!.Nutrition;
            Assert.Empty(purchasedFood.StockConsumed);Assert.Equal(18,purchasedFood.CashRequired);
            Assert.Equal(1,withoutStock.Inventory[1796]);Assert.Equal(2,withoutStock.Bank[1796]);
            foodPreferences.Skip(1796);
            var skipped=(await service.LoadAsync(window,"A",default))!;
            Assert.Equal(0,Assert.Single(skipped.Owned).Best!.Level);Assert.Null(skipped.BestFoodValue);
            foodPreferences.Previous();foodPreferences.SetUseStocks(true);
            var restored=(await service.LoadAsync(window,"A",default))!;
            Assert.Equal(3,Assert.Single(restored.Owned).Best!.Nutrition.StockConsumed[1796]);
            var other=(await service.LoadAsync(window,"B",default))!;
            Assert.Empty(other.Owned);Assert.False(other.InventoryKnown);Assert.False(other.BankKnown);
            stock.ClearLive();
            var reopened=(await service.LoadAsync(window,"A",default))!;
            Assert.Empty(reopened.Owned);Assert.False(reopened.InventoryKnown);
        }
        finally{Directory.Delete(folder,true);}
    }
}
