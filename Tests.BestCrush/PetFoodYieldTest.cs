using System.Net;
using BestCrush.Components.Pure;
using BestCrush.Domain.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests.BestCrush;

public sealed class PetFoodYieldTest
{
    [Theory]
    [InlineData(2.0)]
    [InlineData(null)]
    public async Task ShowsFixedYieldAndServerRatioWithoutInputsSaveActionsOrManualGameChoices(double? price)
    {
        using var provider = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var root=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<PetFoodYield>(ParameterView.FromDictionary(new Dictionary<string,object?>
        {
            [nameof(PetFoodYield.Food)]=new PetFoodDefinition{DofusDbId=10,Name="Aliment",Experience=.5,ExperienceSource="Calcul client"},
            [nameof(PetFoodYield.UnitPrice)]=price
        })));
        string html=WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.Contains("XP par ressource",html);Assert.Contains("XP par kama",html);
        Assert.Contains(price is null ? "—" : (0.25).ToString("0.#####"),html);
        Assert.DoesNotContain("<input",html);Assert.DoesNotContain("<button",html);
        Assert.DoesNotContain("Enregistrer",html);Assert.DoesNotContain("xp-origin",html);
    }
}
