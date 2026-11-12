using System.Net;
using BestCrush.Components.Pages;
using BestCrush.Components.Pure;
using BestCrush.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests.BestCrush;

public sealed class CharacterStockControlsTest
{
    private sealed class Capture : IComponentActivator
    {
        public readonly List<CharacterSheetCard> Cards=[];
        public IComponent CreateInstance(Type type)
        {
            var component=(IComponent)Activator.CreateInstance(type)!;
            if(component is CharacterSheetCard card) Cards.Add(card);
            return component;
        }
    }
    private sealed class Navigation(CurrentDofusClientsState clients) : NavigationManager
    {
        public DofusClientWindowKey? SelectedAtNavigation;
        public string? Destination;
        public Navigation() : this(new CurrentDofusClientsState()) { }
        public void Start() => Initialize("http://localhost/", "http://localhost/profile");
        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            SelectedAtNavigation=clients.SelectedClient?.Key;
            Destination=uri;
        }
    }
    [Fact]
    public async Task EachCardTargetsItsOwnCharacterBeforeOpeningStocksAndNoGlobalActionRemains()
    {
        var a=new DofusClientWindowKey(111,1111);var b=new DofusClientWindowKey(222,2222);
        var clients=new CurrentDofusClientsState();
        var capture=new Capture();var navigation=new Navigation(clients);navigation.Start();
        var services=new ServiceCollection();services.AddLogging();
        services.AddSingleton(clients);services.AddSingleton(new CurrentCharacterState());
        services.AddSingleton(new CurrentAccountProfessionsState());
        services.AddSingleton(new CurrentWindowProfessionsState(new CurrentServerState()));
        services.AddSingleton(new CurrentCandidateWindowCardsState());
        services.AddSingleton(new WindowServerAttributionState());
        services.AddSingleton(new ServerViewFilterState());
        services.AddSingleton<NavigationManager>(navigation);services.AddSingleton<IComponentActivator>(capture);
        await using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var root=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<CharacterProfile>());
        async Task<string> Html()=>WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.DoesNotContain("Voir les stocks",await Html());
        await renderer.Dispatcher.InvokeAsync(()=>clients.Update([
            new(new IntPtr(a.Handle),"Pagalom - Eniripsa - 3.7.12.12 - Release","Dofus",0,0,100,100){ProcessId=a.ProcessId},
            new(new IntPtr(b.Handle),"Autre - Iop - 3.7.12.12 - Release","Dofus",0,0,100,100){ProcessId=b.ProcessId}],a));
        string html=await Html();
        Assert.Equal(2,capture.Cards.Count);
        Assert.Contains("Voir les stocks de Pagalom",html);Assert.Contains("Voir les stocks de Autre",html);
        Assert.DoesNotContain("profile-stocks-link",html);
        Assert.DoesNotContain("sheet-wallets",html);
        Assert.DoesNotContain("Non observés",html);
        await renderer.Dispatcher.InvokeAsync(()=>capture.Cards.Single(card=>card.DisplayName=="Autre").OnViewStocks.InvokeAsync());
        Assert.Equal(b,navigation.SelectedAtNavigation);Assert.Equal("/stocks",navigation.Destination);
        await renderer.Dispatcher.InvokeAsync(()=>capture.Cards.Single(card=>card.DisplayName=="Pagalom").OnViewStocks.InvokeAsync());
        Assert.Equal(a,navigation.SelectedAtNavigation);
        await renderer.Dispatcher.InvokeAsync(()=>clients.Update([],null));
        Assert.DoesNotContain("Voir les stocks",await Html());
    }
}
