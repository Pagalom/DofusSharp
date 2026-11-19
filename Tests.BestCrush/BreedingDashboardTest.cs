using System.Net;
using BestCrush.Components.Pages;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests.BestCrush;

public sealed class BreedingDashboardTest
{
    [Fact]
    public void SynchronizationWarningOnlyAppearsForMissingIncompleteOrOlderThanTwoHours()
    {
        var now=DateTime.UtcNow;
        Assert.False(BreedingDashboardPresentation.NeedsSynchronization(now.AddHours(-2),true,now));
        Assert.True(BreedingDashboardPresentation.NeedsSynchronization(now.AddHours(-2).AddSeconds(-1),true,now));
        Assert.True(BreedingDashboardPresentation.NeedsSynchronization(null,true,now));
        Assert.True(BreedingDashboardPresentation.NeedsSynchronization(now,false,now));
        Assert.Equal("À vérifier",BreedingDashboardPresentation.Duration(now.AddSeconds(-1),now));
    }
    [Fact]
    public async Task SelectingTheSameMountOnlyFlipsItsOwnEnclosureAndPreservesRecordedStats()
    {
        var id=Guid.NewGuid();
        var reading=new PaddockMountReading(id,96,150479,-1234,8020)
            {Endurance=20000,Love=6500,Sex=BreedingMountSex.Male,Fertility=BreedingMountFertility.Sterile,SpeciesId=18};
        var mount=new BreedingMountDisplay(id,7,reading,new(1,"Dragodinde Dorée",1,1),5498,[]);
        string front=await Render([mount],null),back=await Render([mount],id),other=await Render([],id);
        Assert.DoesNotContain("is-flipped",front);Assert.Contains("is-flipped",back);Assert.DoesNotContain("is-flipped",other);
        Assert.Contains("gender-male",front);Assert.Contains("dragodinde.svg",front);Assert.Contains("G1",front);
        Assert.Contains("img/dofus/mounts/33011.png",front);Assert.Contains("img/dofus/mounts/33011.png",back);
        Assert.Contains("breeding-tile-level",front);Assert.Contains("Niv. 96",front);Assert.Contains("is-sterile",front);
        Assert.Contains("Mâle · Stérile",front);Assert.DoesNotContain("breeding-mount-number",front);Assert.DoesNotContain("#7",front);
        Assert.Contains("Stérile",back);Assert.Contains("Sérénité",back);Assert.Contains("-1234",back);
        Assert.Contains("role=\"progressbar\"",back);Assert.Contains("aria-valuenow=\"8020\"",back);
        Assert.DoesNotContain("<input",back);Assert.Contains("Voir les actions",back);
        Assert.Contains("inert",front);Assert.Contains("inert",back);
        Assert.Contains("aria-hidden=\"true\"",front);Assert.Contains("aria-hidden=\"false\"",back);
        var fertile=mount with{Reading=reading with{Fertility=BreedingMountFertility.Fertile}};
        string incomplete=await Render([fertile],null);
        Assert.Contains("breeding-tile-fertility needs-training",incomplete);Assert.DoesNotContain("breeding-tile-needs",incomplete);
        string complete=await Render([fertile with{Reading=fertile.Reading! with{Endurance=20000,Maturity=20000,Love=20000}}],null);
        Assert.Contains("breeding-tile-fertility is-fertile",complete);Assert.DoesNotContain("needs-training",complete);
        Assert.DoesNotContain("needs-training",front);
    }
    [Fact]
    public async Task UnknownStableProfilesCanBeOpenedWithoutInventingTheirSexGenerationOrValue()
    {
        var id=Guid.NewGuid();var mount=new BreedingMountDisplay(id,3,null,null,null,[]);
        string html=await Render([mount],id);
        Assert.Contains("Sexe inconnu",html);Assert.Contains("Fertilité inconnue",html);Assert.Contains("G?",html);
        Assert.Contains("Non relevé",html);Assert.DoesNotContain("gender-male",html);Assert.DoesNotContain("gender-female",html);
    }
    [Fact]
    public async Task EnclosureTanksShowFuelCapacityAndDistinguishEmptyFromUnobserved()
    {
        string html=await Render([],null,[new(PaddockGauge.Love,true,0),new(PaddockGauge.Maturity,false,80000)]);
        Assert.Equal(6,System.Text.RegularExpressions.Regex.Matches(html,"role=\"meter\"").Count);
        Assert.Contains("aria-valuemax=\"200000\"",html);Assert.Contains("height:40%",html);
        Assert.Contains("tone-love is-active",html);Assert.Contains("aria-valuenow=\"0\"",html);
        Assert.Contains("is-unknown",html);Assert.DoesNotContain("breeding-fuel-strip",html);
        Assert.True(html.IndexOf("tone-serenityloss",StringComparison.Ordinal)<html.IndexOf("tone-serenitygain",StringComparison.Ordinal));
    }
    [Fact]
    public void EachBreedingSpeciesUsesItsOwnMarketItemImageIncludingCrossedColours()
    {
        Assert.Equal(306,BreedingLineage.Rides.Count);
        foreach(var ride in BreedingLineage.Rides.Values)
            Assert.Equal($"img/dofus/mounts/{ride.ItemId}.png",BreedingDashboardPresentation.Head(ride.Id));
        Assert.Equal("img/breeding/muldo.svg",BreedingDashboardPresentation.Head(null,2));
        Assert.Equal("img/mount.svg",BreedingDashboardPresentation.Head(long.MaxValue));
    }
    [Fact]
    public void OrangeIndicatorUsesTheThreeRecordedBreedingBarsAndDoesNotInventUnknownRequirements()
    {
        var full=new PaddockMountReading(Guid.NewGuid(),1,0,-1500,20000){Endurance=20000,Love=20000};
        Assert.False(BreedingDashboardPresentation.NeedsBreedingTraining(full));
        Assert.True(BreedingDashboardPresentation.NeedsBreedingTraining(full with{Endurance=19999}));
        Assert.True(BreedingDashboardPresentation.NeedsBreedingTraining(full with{Maturity=0}));
        Assert.True(BreedingDashboardPresentation.NeedsBreedingTraining(full with{Love=19999}));
        Assert.False(BreedingDashboardPresentation.NeedsBreedingTraining(null));
        Assert.False(BreedingDashboardPresentation.NeedsBreedingTraining(full with{Love=null}));
    }
    [Fact]
    public void MountHoverHasExactlyFourIndicatorsWithoutRepeatingLevelSexOrFertilityAndEscapesNames()
    {
        var reading=new PaddockMountReading(Guid.NewGuid(),96,0,-1500,20000){Endurance=10000,Love=0,Sex=BreedingMountSex.Male,Fertility=BreedingMountFertility.Fertile};
        var mount=new BreedingMountDisplay(reading.Id,7,reading,new(18,"Dorée <script>",1,1),null,[]);
        string html=BreedingDashboardPresentation.MountTooltipHtml(mount);
        Assert.Equal(4,System.Text.RegularExpressions.Regex.Matches(html,"role=\"img\"").Count);
        Assert.Contains("need-serenity",html);Assert.Contains("need-endurance is-partial",html);Assert.Contains("need-maturity is-full",html);Assert.Contains("need-love is-empty",html);
        Assert.Contains("&lt;script&gt;",html);Assert.DoesNotContain("<script>",html);
        Assert.DoesNotContain("Niv.",html);Assert.DoesNotContain("Mâle",html);Assert.DoesNotContain("Fertile",html);
    }
    private static async Task<string> Render(IReadOnlyList<BreedingMountDisplay> mounts,Guid? selected,IReadOnlyList<PaddockGaugeReading>? gauges=null)
    {
        var services=new ServiceCollection();services.AddLogging();await using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var root=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<BreedingEnclosureCard>(ParameterView.FromDictionary(new Dictionary<string,object?>
            {{"Title","Enclos 1"},{"LocationKey","enclosure-1"},{"Mounts",mounts},{"SelectedMount",selected},{"Gauges",gauges ?? []},{"ObservedAt",DateTime.UtcNow}})));
        return WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
    }
}
