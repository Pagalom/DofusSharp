using System.Net;
using BestCrush.Components.Pages;
using BestCrush.Domain;
using BestCrush.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.BestCrush.Utils;

namespace Tests.BestCrush;

public sealed class StockWalletDisplayTest
{
    [Fact]
    public async Task WalletUsesTheTargetWindowAndDoesNotShowUnknownPlaceholderAmounts()
    {
        var a=new DofusClientWindowKey(111,1111);var b=new DofusClientWindowKey(222,2222);var unknown=new DofusClientWindowKey(333,3333);
        var clients=new CurrentDofusClientsState();
        clients.Update([Window(a,"Alice"),Window(b,"Bob"),Window(unknown,"Inconnu")],a);
        var characters=new CurrentCharacterState();characters.SelectServer("Kourial");
        CharacterIdentityAssessment Confirm(DofusClientWindowKey window, ulong id, string name)
        {
            var scope=new CharacterConnectionScope("Kourial",name,1,1,1);
            var correlator=new CharacterIdentityCorrelator();
            correlator.Observe(new(scope,CharacterIdentityEvidenceSource.CharacterSelection,id,name,4,150,DateTime.UtcNow));
            var identity=correlator.Observe(new(scope,CharacterIdentityEvidenceSource.CharacterDetails,id,name,4,150,DateTime.UtcNow));
            Assert.True(characters.ObserveConfirmedCharacter(identity,false,window));
            return identity;
        }
        var alice=Confirm(a,123,"Alice");var bob=Confirm(b,456,"Bob");
        Assert.True(characters.ApplyObservedData(alice,new(alice.Scope,123,PocketKamas:25000,BankKamas:100000)));
        Assert.True(characters.ApplyObservedData(bob,new(bob.Scope,456,PocketKamas:500,BankKamas:0)));
        characters.SelectCharacter(new("Kourial",456)); // active profile alone must not determine the wallet
        using var db=new TestDatabase();
        string folder=Path.Combine(Path.GetTempPath(),"stock-wallet-"+Guid.NewGuid());
        try
        {
            var stocks=new LiveStockService(new LiveStockRepository(Path.Combine(folder,"stocks.db")),clients);
            var services=new ServiceCollection();services.AddLogging();services.AddSingleton(stocks);services.AddSingleton(clients);
            services.AddSingleton(characters);services.AddSingleton<IDbContextFactory<BestCrushDbContext>>(db);
            await using var provider=services.BuildServiceProvider();
            await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
            var root=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<Stocks>());
            async Task<string> Html()=>WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
            string html=await Html();Assert.Contains(25000.ToString("N0")+" K",html);Assert.Contains(100000.ToString("N0")+" K",html);
            await renderer.Dispatcher.InvokeAsync(()=>clients.SelectWindow(b));
            html=await Html();Assert.Contains(500.ToString("N0")+" K",html);Assert.Contains(0.ToString("N0")+" K",html);
            Assert.DoesNotContain(25000.ToString("N0")+" K",html);
            await renderer.Dispatcher.InvokeAsync(()=>clients.SelectWindow(unknown));
            html=await Html();Assert.DoesNotContain("stock-wallet-summary",html);Assert.DoesNotContain("Non observés",html);
        }
        finally { if(Directory.Exists(folder))Directory.Delete(folder,true); }
    }
    private static DofusWindowInfo Window(DofusClientWindowKey key,string name)=>new(new IntPtr(key.Handle),$"{name} - Eniripsa - 3.7.12.12 - Release","Dofus",0,0,100,100){ProcessId=key.ProcessId};
}
