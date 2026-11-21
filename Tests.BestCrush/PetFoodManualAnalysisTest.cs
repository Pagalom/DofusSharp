using BestCrush.Components.Pure;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.BestCrush.Utils;

namespace Tests.BestCrush;

public sealed class PetFoodManualAnalysisTest
{
    private static LivePetFeedingService Service(TestDatabase db)=>new(db,new(),new(),new(),NullLogger<LivePetFeedingService>.Instance);
    private static PetFoodDefinition Food(long id)=>new(){DofusDbId=id,Name=$"Aliment {id}",MarketType=MarketObjectType.Resource};

    [Fact]
    public async Task ManualInputUsesSameCoefficientAsAutomaticMealAndRefreshesTheWeeklyQueue()
    {
        using var database=new TestDatabase();var service=Service(database);
        string? changed=null;long? updated=null;
        service.Changed+=server=>changed=server;service.CoefficientUpdated+=id=>updated=id;
        var batch=new PetFoodAnalysisBatch([Food(10),Food(20)]);
        service.CoefficientUpdated+=id=>batch.Complete(id);
        Assert.True(await service.RecordAsync(Food(10),3,1));
        Assert.Equal("",changed);Assert.Equal(10,updated);Assert.Equal(20,batch.Current!.DofusDbId);
        using var db=database.CreateContext();
        var food=await db.PetFoods.SingleAsync();
        Assert.Equal(1.0/3,food.Experience);Assert.NotNull(food.ExperienceObservedAtUtc);Assert.Equal(MarketPriceSource.Manual,food.ExperienceOrigin);
        Assert.Equal("Aliment 10",food.Name);Assert.Equal(food.Experience,(await db.Resources.SingleAsync()).PetExperience);
        Assert.Empty(PetOpportunityService.FoodTests([food],new Dictionary<long,System.Numerics.BigInteger>(),new Dictionary<long,System.Numerics.BigInteger>()));
        Assert.True(await LivePetFeedingService.SaveAsync(db,new(1,10,12133,10,100,105,DateTime.UtcNow.AddSeconds(1)),default));
        Assert.Equal(.5,food.Experience);Assert.Equal(.5,(await db.Resources.SingleAsync()).PetExperience);Assert.Equal(MarketPriceSource.InGameAutomatic,food.ExperienceOrigin);
        Assert.False(await service.RecordAsync(Food(20),0,1));
        Assert.False(await service.RecordAsync(Food(20),1,-1));
        var rune=Food(20);rune.MarketType=MarketObjectType.Rune;
        Assert.False(await service.RecordAsync(rune,1,1));
    }

    [Fact]
    public void QueueCapsAtFiftySkipsRecentTestsAndKeepsCurrentWhenAnotherFoodIsLearned()
    {
        var fresh=Food(70);fresh.ExperienceObservedAtUtc=DateTime.UtcNow;
        var batch=new PetFoodAnalysisBatch(new[]{fresh}.Concat(Enumerable.Range(1,60).Select(i=>Food(i))));
        Assert.Equal(50,batch.Remaining.Count);Assert.Equal(1,batch.Current!.DofusDbId);
        batch.Move(2);Assert.Equal(3,batch.Current!.DofusDbId);
        batch.Complete(1);Assert.Equal(3,batch.Current!.DofusDbId);
        batch.Complete(3);Assert.Equal(4,batch.Current!.DofusDbId);
        batch.Move(-1);Assert.Equal(2,batch.Current!.DofusDbId);
        foreach(var food in batch.Remaining.ToArray())batch.Complete(food.DofusDbId);
        Assert.Null(batch.Current);batch.Move(1);Assert.Equal(0,batch.Position);
    }

    [Fact]
    public async Task AnalysisFormRendersTheProposedResourceAndOnlyTheTwoRequestedInputs()
    {
        using var database=new TestDatabase();
        using var provider=new ServiceCollection().AddLogging().AddSingleton(Service(database))
            .AddSingleton(new MarketClipboardQueue(new DofusWindowService(),NullLogger<MarketClipboardQueue>.Instance))
            .AddSingleton(new ItemsService(null!,null!,null!,null!,null!)).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var root=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<PetFoodAnalysis>(ParameterView.FromDictionary(
            new Dictionary<string,object?>{{nameof(PetFoodAnalysis.Foods),new[]{Food(10),Food(20)}}})));
        string html=await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.Contains("Aliment 10",html);Assert.DoesNotContain("Aliment 20",html);
        Assert.Equal(2,html.Split("type=\"number\"").Length-1);
        Assert.Contains("XP obtenue",System.Net.WebUtility.HtmlDecode(html));Assert.DoesNotContain("class=\"food-copy\"",html);Assert.Contains("Enregistrer",html);
    }
}
