using System.Net;
using BestCrush.Components.Pure;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests.BestCrush;

public sealed class PetFoodSelectorTest
{
    private sealed class Capture:IComponentActivator
    {
        public PetFoodSelector Selector=null!;
        public IComponent CreateInstance(Type type)
        {var result=(IComponent)Activator.CreateInstance(type)!;if(result is PetFoodSelector selector)Selector=selector;return result;}
    }

    [Fact]
    public async Task KeepsTheResourceDuringLoadingReservesAllThreeButtonsAndClearsItAcrossContexts()
    {
        var capture=new Capture();
        using var provider=new ServiceCollection().AddLogging().AddSingleton<IComponentActivator>(capture)
            .AddSingleton(new ItemsService(null!,null!,null!,null!,null!)).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        PetFoodValue Food(long id,string name)=>new(new(id,name,null,MarketObjectType.Resource,5,false),10,1);
        var first=Food(1,"Premier aliment");var second=Food(2,"Un nom très long pour le deuxième aliment");
        var root=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<PetFoodSelector>(ParameterView.FromDictionary(new Dictionary<string,object?>
        {{nameof(PetFoodSelector.Value),first},{nameof(PetFoodSelector.ContextKey),"A"}})));
        async Task<string> Html()=>WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        void Controls(string html)
        {
            Assert.Contains("aria-label=\"Revenir au meilleur aliment\"",html);
            Assert.Contains("aria-label=\"Aliment précédent\"",html);Assert.Contains("aria-label=\"Aliment suivant\"",html);
            Assert.Contains("food-summary",html);Assert.Contains("food-artwork",html);
        }
        var html=await Html();Controls(html);Assert.Contains("Premier aliment",html);Assert.Contains("Meilleure ressource à utiliser",html);
        Assert.Contains("XP/K",html);Assert.Contains("K/u",html);Assert.Contains("trade-item-image",html);
        async Task Update(PetFoodValue? value,bool loading,string context,int skipped=0)=>await renderer.Dispatcher.InvokeAsync(()=>capture.Selector.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string,object?>
        {{nameof(PetFoodSelector.Value),value},{nameof(PetFoodSelector.Loading),loading},{nameof(PetFoodSelector.ContextKey),context},{nameof(PetFoodSelector.SkippedCount),skipped}})));
        await Update(null,true,"A",1);html=await Html();Controls(html);Assert.Contains("Premier aliment",html);Assert.Contains("aria-busy=\"true\"",html);
        await Update(second,false,"A",1);html=await Html();Controls(html);Assert.Contains(second.Food.Name,html);Assert.DoesNotContain("Premier aliment",html);Assert.Contains("Ressource sélectionnée",html);
        await Update(null,true,"B");html=await Html();Controls(html);Assert.DoesNotContain(second.Food.Name,html);Assert.Contains("Calcul en cours",html);
        await Update(null,false,"B");html=await Html();Controls(html);Assert.DoesNotContain("Calcul en cours…",html);
    }
}
