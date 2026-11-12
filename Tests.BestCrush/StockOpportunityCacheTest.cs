using System.Data.Common;
using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tests.BestCrush.Utils;
using static Tests.BestCrush.Utils.EconomicTestData;

namespace Tests.BestCrush;

public sealed class StockOpportunityCacheTest
{
    private sealed class Queries : DbCommandInterceptor
    {
        public readonly List<string> Text=[];
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result)
        {Text.Add(command.CommandText);return result;}
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData data,
            InterceptionResult<DbDataReader> result,CancellationToken token=default)
        {Text.Add(command.CommandText);return ValueTask.FromResult(result);}
    }
    private sealed class Source(string origin) : IOpportunityProvider
    {
        public string Origin=>origin;
        public int Calls;
        public OpportunityScanContext? Last;
        public bool HadSynchronizationContext;
        public Task<IReadOnlyList<EconomicOpportunity>> EvaluateAsync(OpportunityScanContext context,CancellationToken token)
        {
            Calls++;Last=context;HadSynchronizationContext|=SynchronizationContext.Current is not null;
            return Task.FromResult<IReadOnlyList<EconomicOpportunity>>([]);
        }
    }
    private static LiveStockSource Stock(ulong quantity=6)=>new("pagalom-session","Pagalom",Server,DateTime.UtcNow,
        [new LiveStockContainer("inventory",true,false,DateTime.UtcNow,[new(1,10,quantity,null,null,null)])]);

    [Fact]
    public async Task CraftSkipsCrushingAndSqlOnCacheHitButInvalidatesPricesStocksScopesAndManualRefresh()
    {
        using TestDatabase database=new();using var seed=database.CreateContext();
        var equipment=new Equipment(100){Name="Objet",Level=10};equipment.Recipe.Add(new(equipment,new Resource(1){Name="Bois"},10));
        seed.Equipments.Add(equipment);
        seed.MarketPriceObservations.AddRange(Price(1,1,10,at:DateTime.UtcNow),Price(100,1,100,type:MarketObjectType.Equipment),
            Price(500,1,100,type:MarketObjectType.Rune));await seed.SaveChangesAsync();
        var queries=new Queries();using var db=new BestCrushDbContext(new DbContextOptionsBuilder<BestCrushDbContext>()
            .UseSqlite(seed.Database.GetDbConnection()).AddInterceptors(queries).Options);
        var priority=new TestDataPriorityProvider();var prices=new MarketPriceService(db,priority);
        var craft=new Source("craft");var crushing=new Source("concassage");var notifier=new MarketDataChangeNotifier();
        using var scanner=new StockOpportunityService(new ItemsService(db,null!,null!,null!,null!),
            new EquipmentProfitabilityService(null!,null!,new CoefficientService(db,priority),prices,null!,null!),
            new StockCraftPlanner(prices),[craft,crushing],notifier,priority:priority);
        var stock=Stock();
        var first=await scanner.ScanAsync(stock,Server,1,default,"craft",useCache:true);
        Assert.Equal(1,craft.Calls);Assert.Equal(0,crushing.Calls);Assert.False(craft.HadSynchronizationContext);
        Assert.Empty(craft.Last!.Market.RunePrices);Assert.Empty(craft.Last.Market.Coefficients);
        Assert.DoesNotContain(queries.Text,x=>x.Contains("CoefficientObservations"));
        Assert.Equal(40,craft.Last.Plans.Single().CashRequired);
        queries.Text.Clear();
        Assert.Same(first,await scanner.ScanAsync(stock,Server.ToLowerInvariant(),1,default,"craft",useCache:true));
        Assert.Empty(queries.Text);Assert.Equal(1,craft.Calls);
        notifier.Notify(MarketObjectType.Rune,500,Server);
        Assert.Same(first,await scanner.ScanAsync(stock,Server,1,default,"craft",useCache:true));
        Assert.Empty(queries.Text);Assert.Equal(1,craft.Calls);

        // Same timestamp, different container snapshot: recalculates stock only, using cached SQL inputs.
        var changed=stock with{Containers=Stock(7).Containers};
        await scanner.ScanAsync(changed,Server,1,default,"craft",useCache:true);
        Assert.Equal(2,craft.Calls);Assert.Equal(30,craft.Last!.Plans.Single().CashRequired);Assert.Empty(queries.Text);

        seed.MarketPriceObservations.Add(Price(1,1,20,at:DateTime.UtcNow.AddMinutes(1)));await seed.SaveChangesAsync();
        notifier.Notify(MarketObjectType.Resource,1,Server);
        var updated=await scanner.ScanAsync(changed,Server,1,default,"craft",useCache:true);
        Assert.Equal(3,craft.Calls);Assert.Equal(60,craft.Last!.Plans.Single().CashRequired);
        Assert.NotEmpty(queries.Text);Assert.DoesNotContain(queries.Text,x=>x.Contains("FROM \"Equipments\""));
        queries.Text.Clear();notifier.Notify(MarketObjectType.Resource,1,"OTHER_SERVER");
        Assert.Same(updated,await scanner.ScanAsync(changed,Server,1,default,"craft",useCache:true));Assert.Empty(queries.Text);

        var another=changed with{Id="another-session"};
        var isolated=await scanner.ScanAsync(another,Server,1,default,"craft",useCache:true);
        Assert.Equal("another-session",isolated.SourceId);Assert.NotSame(updated,isolated);
        await scanner.ScanAsync(changed,Server,2,default,"craft",useCache:true);
        Assert.Equal(2,craft.Last!.Plans.Single().Quantity);

        await scanner.ScanAsync(changed,Server,1,default,useCache:true);
        Assert.Equal(1,crushing.Calls);Assert.NotEmpty(crushing.Last!.Market.RunePrices);
        Assert.Contains(queries.Text,x=>x.Contains("CoefficientObservations"));
        queries.Text.Clear();
        await scanner.ScanAsync(changed,Server,1,default,"craft",useCache:true,forceRefresh:true);
        Assert.Contains(queries.Text,x=>x.Contains("FROM \"Equipments\""));
        Assert.Equal(1,crushing.Calls);
    }

    [Fact]
    public async Task CancelledAndInvalidInputsCannotReturnACachedReport()
    {
        using TestDatabase database=new();using var db=database.CreateContext();var priority=new TestDataPriorityProvider();
        var prices=new MarketPriceService(db,priority);var craft=new Source("craft");
        using var scanner=new StockOpportunityService(new ItemsService(db,null!,null!,null!,null!),
            new EquipmentProfitabilityService(null!,null!,new CoefficientService(db,priority),prices,null!,null!),new StockCraftPlanner(prices),[craft]);
        var stock=Stock();await scanner.ScanAsync(stock,Server,1,default,"craft",useCache:true);
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>scanner.ScanAsync(stock,Server,1,cancel.Token,"craft",useCache:true));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>scanner.ScanAsync(stock,"OTHER_SERVER",1,default,"craft",useCache:true));
        Assert.Equal(1,craft.Calls);
    }
}
