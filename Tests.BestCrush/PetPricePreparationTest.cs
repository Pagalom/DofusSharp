using System.Net;
using System.Reflection;
using System.Numerics;
using BestCrush.Components.Pages;
using BestCrush.Components.Overlays;
using BestCrush.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.BestCrush;

public sealed class PetPricePreparationTest
{
    [Fact]
    public async Task PageRetainsBothPriceQueuesAndPlacesEmptyCardsLastWithoutXpForms()
    {
        using var provider=new ServiceCollection().AddLogging()
            .AddSingleton(new MarketClipboardQueue(new DofusWindowService(),NullLogger<MarketClipboardQueue>.Instance)).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var root=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<PetPreparationLists>(ParameterView.FromDictionary(new Dictionary<string,object?>
        {
            [nameof(PetPreparationLists.ResourcePriceChecks)]=Enumerable.Range(1,25).Select(i=>new BreedingPriceCheck(i,i==1?"Bec <Dodox>":$"Ressource {i}",null,"Prix manquant")).ToArray(),
            [nameof(PetPreparationLists.ShowResourcePrices)]=true
        })));
        string html=await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.Contains("Bec &lt;Dodox&gt;",html);Assert.Contains("Ressource 20",html);Assert.DoesNotContain("Ressource 21",html);
        html=WebUtility.HtmlDecode(html);
        Assert.Equal(2,html.Split("type=\"search\"").Length-1);
        Assert.True(html.IndexOf("aria-label=\"Prix ressources à actualiser\"",StringComparison.Ordinal)<html.IndexOf("aria-label=\"Prix familiers à actualiser\"",StringComparison.Ordinal));
        Assert.DoesNotContain("Ressources à tester",html);Assert.DoesNotContain("type=\"number\"",html);Assert.DoesNotContain("Enregistrer",html);
    }

    [Fact]
    public async Task OverlayKeepsBothPriceCopyActionsAndFiftyResourceLimit()
    {
        var checks=Enumerable.Range(1,65).Select(i=>new BreedingPriceCheck(i,$"Aliment {i:00}",null,"Ancien")).ToArray();
        var known=new HashSet<long>{64,65};
        var report=new PetOpportunityReport("Kourial",[],[],[],new Dictionary<long,BigInteger>(),new Dictionary<long,BigInteger>(),true,true,true,true,0,0)
        {FoodPriceChecks=checks,KnownFoodExperienceItems=known,PetPriceChecks=[new(100,"Familier prioritaire",null,"Ancien")]};
        string[] copied=[];
        var component=new PetMarketOverlay{Report=report,CopyRequested=EventCallback.Factory.Create<IEnumerable<string>>(this,(IEnumerable<string> names)=>copied=names.ToArray())};
        async Task Click(string method)=>await (Task)typeof(PetMarketOverlay).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(component,null)!;
        await Click("CopyResourcePrices");Assert.Equal(50,copied.Length);Assert.Equal("Aliment 64",copied[0]);
        await Click("CopyPetPrices");Assert.Equal(new[]{"Familier prioritaire"},copied);
    }
}
