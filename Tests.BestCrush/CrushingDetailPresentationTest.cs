using System.Net;
using BestCrush.Components.Pure;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests.BestCrush;

public sealed class CrushingDetailPresentationTest
{
    [Fact]
    public async Task MissingPricesAreGroupedByActualObjectsAndDoNotExposeOldExplanations()
    {
        var equipment = new Equipment(100) { Name = "Cape test" };
        var resource = new Resource(1) { Name = "Ferrite" };
        equipment.Recipe.Add(new(equipment, resource, 3));
        var rune = new Rune(10) { Name = "Rune Fo", Characteristic = Characteristic.Strength };
        var cost = new CraftCostResult([new(1, resource.Name, 3, null)]);
        var result = new EquipmentProfitabilityResult(equipment, null, null, cost, [],
            ["Craft incomplet : 1 prix d'ingrédient manquant(s)", "Prix de rune manquant : Rune Fo", "Prix local de l'équipement manquant"])
            { PotentialRunes = [rune] };
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton(new ItemsService(null!,null!,null!,null!,null!));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var component = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<CrushingMissingData>(
            ParameterView.FromDictionary(new Dictionary<string,object?> { ["Calculation"] = result, ["Messages"] = result.MissingData })));
        string html = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(component.ToHtmlString));
        Assert.Contains("Ressource(s) :", html); Assert.Contains("Rune(s) :", html); Assert.Contains("Équipement(s) :", html);
        Assert.Contains("Ferrite : prix manquant", html); Assert.Contains("Rune Fo : prix manquant", html);
        Assert.DoesNotContain("meilleur scénario connu", html); Assert.DoesNotContain("Certaines possibilités", html);
        Assert.DoesNotContain("Craft incomplet", html);
    }
}
