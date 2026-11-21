using System.Reflection;
using System.Net;
using BestCrush.Components.Pure;
using BestCrush.Domain.Models;
using BestCrush.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.BestCrush.Utils;

namespace Tests.BestCrush;

public sealed class PetFoodExperienceEntryTest
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    private static void Input(PetFoodExperienceEntry editor,int? quantity,long? xp)
    {typeof(PetFoodExperienceEntry).GetField("_quantity",Hidden)!.SetValue(editor,quantity);typeof(PetFoodExperienceEntry).GetField("_experience",Hidden)!.SetValue(editor,xp);}
    private static Task Save(PetFoodExperienceEntry editor)=>(Task)typeof(PetFoodExperienceEntry).GetMethod("Save",Hidden)!.Invoke(editor,null)!;

    [Fact]
    public async Task EnterAndBlurShareOneSaveRejectIncompleteInputsAndDoNotPersistRawAmounts()
    {
        using var database=new TestDatabase();var feeding=new LivePetFeedingService(database,new(),new(),new(),NullLogger<LivePetFeedingService>.Instance);
        int updates=0;feeding.CoefficientUpdated+=_=>updates++;
        using var provider=new ServiceCollection().AddSingleton(feeding).BuildServiceProvider();
        var food=new PetFoodDefinition{DofusDbId=10,Name="Aliment",MarketType=MarketObjectType.Resource};
        var editor=new PetFoodExperienceEntry{Food=food};
        typeof(PetFoodExperienceEntry).GetProperty("Services",Hidden)!.SetValue(editor,provider);
        Input(editor,3,null);await Save(editor);Assert.Equal(0,updates);
        Input(editor,3,1);
        await (Task)typeof(PetFoodExperienceEntry).GetMethod("KeyDown",Hidden)!.Invoke(editor,[new KeyboardEventArgs{Key="Enter"}])!;
        await Save(editor);await Save(editor);Assert.Equal(1,updates);
        using var db=database.CreateContext();var stored=Assert.Single(await db.PetFoods.ToArrayAsync());
        Assert.Equal(1.0/3,stored.Experience);Assert.Equal(MarketPriceSource.Manual,stored.ExperienceOrigin);
        Assert.NotNull(stored.ExperienceObservedAtUtc);
        Input(editor,6,2);await Save(editor);Assert.Equal(2,updates);
        Input(editor,0,1);await Save(editor);Input(editor,1,-1);await Save(editor);Assert.Equal(2,updates);
        food.Experience=.5;food.ExperienceOrigin=MarketPriceSource.InGameAutomatic;
        Input(editor,6,2);await Save(editor);Assert.Equal(3,updates);Assert.Equal(MarketPriceSource.Manual,food.ExperienceOrigin);
        food.ExperienceObservedAtUtc=DateTime.UtcNow.AddDays(-8);await Save(editor);Assert.Equal(4,updates);
        editor.ReadOnly=true;Input(editor,1,100);await Save(editor);Assert.Equal(4,updates);
        Assert.DoesNotContain(db.Model.FindEntityType(typeof(PetFoodDefinition))!.GetProperties(),p=>p.Name is "Quantity" or "ExperienceBefore" or "ExperienceAfter");
    }

    [Fact]
    public async Task DisplaysReadonlyKamasPerXpAndOriginBesideTwoGroupedInputs()
    {
        using var provider=new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var root=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<PetFoodExperienceEntry>(ParameterView.FromDictionary(new Dictionary<string,object?>
        {
            [nameof(PetFoodExperienceEntry.Food)]=new PetFoodDefinition{DofusDbId=10,Name="Aliment",MarketType=MarketObjectType.Resource,Experience=.5,ExperienceOrigin=MarketPriceSource.InGameAutomatic},
            [nameof(PetFoodExperienceEntry.UnitPrice)]=2.0,[nameof(PetFoodExperienceEntry.ShowRate)]=true
        })));
        string html=WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.Contains("Coût en kamas par XP",html);Assert.Contains((4.0).ToString("N4"),html);
        Assert.Contains(">J</span>",html);Assert.Equal(2,html.Split("type=\"number\"").Length-1);
        Assert.Contains("Quantité · Aliment",html);Assert.Contains("XP obtenue · Aliment",html);Assert.Contains("Enregistrer",html);
    }
}
