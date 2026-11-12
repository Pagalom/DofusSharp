using BestCrush.Domain.Models;
using BestCrush.Domain.Services;

namespace Tests.BestCrush;

public sealed class MarketAnalyticsComparisonTest
{
    private static readonly DateTime Start = new(2026,11,1,12,0,0,DateTimeKind.Utc);
    private static readonly MarketAnalyticsMember A = new(MarketObjectType.Equipment,1,"Petit prix");
    private static readonly MarketAnalyticsMember B = new(MarketObjectType.Equipment,2,"Grand prix");
    private static MarketAnalyticsSeriesDefinition Group(params MarketAnalyticsMember[] members) => new()
    { SelectionMode=MarketAnalyticsSelectionMode.Custom,ItemName="Groupe",Members=members.ToList(),Aggregation=MarketAnalyticsAggregation.Mean };
    private static MarketAnalyticsObservation Obs(MarketAnalyticsMember item,double hour,long price,int lot=1,
        MarketPriceSource source=MarketPriceSource.InGameAutomatic,bool clear=false) => new(item.Key,Start.AddHours(hour),price,lot,source,clear);
    private static MarketAnalyticsComparison Run(MarketAnalyticsSeriesDefinition[] definitions,
        MarketAnalyticsObservation[] observations,int maxAge=72,double hours=2) => MarketAnalyticsCalculator.Calculate(
            new("A",definitions,MarketAnalyticsPeriod.Hours24,Start,Start.AddHours(hours),maxAge),
            definitions.Select(d => (IReadOnlyList<MarketAnalyticsMember>)d.Members).ToArray(),observations);

    [Fact]
    public void NormalizesEachMemberBeforeFusionAndKeepsEqualWeightsDespiteObservationFrequency()
    {
        var group=Group(A,B);
        var observations=new[]{Obs(A,0,10),Obs(B,0,1000),Obs(A,1,20),Obs(B,1,1000)}
            .Concat(Enumerable.Repeat(Obs(A,1,20),100)).ToArray();
        var result=Run([group],observations).Groups.Single();
        Assert.Equal(100,result.FusedSamples[0].Index);
        Assert.Equal(150,result.FusedSamples[1].Index);
        Assert.Equal(510,result.FusedSamples[1].Price);
        Assert.Equal(2,result.BaselineMemberCount);
    }

    [Fact]
    public void DuplicateMembersCannotIncreaseTheirWeight()
    {
        var result=Run([Group(A,A,B)],[Obs(A,0,10),Obs(B,0,1000),Obs(A,1,20),Obs(B,1,1000)]).Groups.Single();
        Assert.Equal(2,result.Members.Count); Assert.Equal(150,result.FusedSamples[1].Index);
    }

    [Fact]
    public void ConvertsLotPricesBeforeComparingAndDoesNotWeightLargeLotsMore()
    {
        var result=Run([Group(A,B)],[Obs(A,0,1000,100),Obs(B,0,1000),Obs(A,1,2000,100),Obs(B,1,1000)]).Groups.Single();
        Assert.Equal(10,result.Members[0].ReferencePrice); Assert.Equal(150,result.FusedSamples[1].Index);
    }

    [Fact]
    public void AllLotsGiveOneVoteToEachLotInsideAnItem()
    {
        var group=Group(A);
        var observations=new[]{Obs(A,0,10),Obs(A,0,100,10),Obs(A,1,20)}
            .Concat(Enumerable.Repeat(Obs(A,1,100,10),100)).ToArray();
        Assert.Equal(15,Run([group],observations).Groups.Single().FusedSamples[1].Price);
    }

    [Fact]
    public void CommonReferenceIsTheSameDateForAllComparedSeries()
    {
        var result=Run([Group(A),Group(B)],[Obs(A,0,10),Obs(A,1,20),Obs(B,1,1000)]);
        Assert.Equal(Start.AddHours(1),result.ReferenceUtc);
        Assert.All(result.Groups,g=>Assert.Equal(100,g.FusedSamples[1].Index));
        Assert.Equal(50,result.Groups[0].FusedSamples[0].Index);
    }

    [Fact]
    public void LateEntrantsStayOutsideTheFixedCohort()
    {
        var later=new MarketAnalyticsMember(MarketObjectType.Equipment,3,"Arrivée tardive");
        var result=Run([Group(A,B,later)],[Obs(A,0,10),Obs(B,0,1000),Obs(A,2,20),Obs(later,2,50)],maxAge:1).Groups.Single();
        Assert.Equal(3,result.Members.Count); Assert.Equal(2,result.BaselineMemberCount);
        Assert.Null(result.Members[2].ReferencePrice); Assert.Null(result.FusedSamples[2].Index);
    }

    [Fact]
    public void ReferencePrefersBroadCoverageOverTheFirstIsolatedQuotation()
    {
        var result=Run([Group(A,B)],[Obs(A,0,10),Obs(A,1,20),Obs(B,1,1000)]);
        Assert.Equal(Start.AddHours(1),result.ReferenceUtc);Assert.Equal(2,result.Groups[0].BaselineMemberCount);
    }

    [Fact]
    public void MissingFreshMemberCreatesAGapInsteadOfChangingTheGroupsComposition()
    {
        var result=Run([Group(A,B)],[Obs(A,0,10),Obs(B,0,1000),Obs(A,2,20)],maxAge:1).Groups.Single();
        Assert.NotNull(result.FusedSamples[1].Index); Assert.Null(result.FusedSamples[2].Index);
        Assert.Equal(1,result.CurrentMemberCount); Assert.Equal(2,result.BaselineMemberCount);
    }

    [Fact]
    public void CarriedQuotesExpireAndDoNotCreateFakeObservations()
    {
        var result=Run([Group(A)],[Obs(A,0,10)],maxAge:1).Groups.Single().Members.Single().Samples;
        Assert.True(result[1].IsCarried); Assert.Equal(0,result[1].ObservationCount);
        Assert.Equal(Start,result[1].LastObservedAtUtc); Assert.Null(result[2].Price);
    }

    [Theory]
    [InlineData(MarketAnalyticsAggregation.Median,100)]
    [InlineData(MarketAnalyticsAggregation.Mean,120)]
    [InlineData(MarketAnalyticsAggregation.FirstQuartile,95)]
    [InlineData(MarketAnalyticsAggregation.ThirdQuartile,135)]
    public void CarryPreservesTheComputedIntervalValueRatherThanItsLastRawQuote(MarketAnalyticsAggregation aggregation,double expected)
    {
        var definition=Group(A);definition.Aggregation=aggregation;
        var samples=Run([definition],[Obs(A,0,100),Obs(A,.2,90),Obs(A,.5,100),Obs(A,.8,170)],maxAge:2,hours:3)
            .Groups.Single().Members.Single().Samples;
        Assert.Equal(expected,samples[1].Price);Assert.Equal(expected,samples[2].Price);
        Assert.Equal(3,samples[1].ObservationCount);Assert.Equal(0,samples[2].ObservationCount);
        Assert.True(samples[2].IsCarried);Assert.Equal(Start.AddHours(.8),samples[2].LastObservedAtUtc);
        Assert.Null(samples[3].Price);
    }

    [Fact]
    public void RefreshingOneGroupMemberKeepsTheOtherMembersComputedMedian()
    {
        var definition=Group(A,B);definition.Aggregation=MarketAnalyticsAggregation.Median;
        var result=Run([definition],[Obs(A,0,100),Obs(B,0,100),Obs(A,.2,90),Obs(A,.8,110),Obs(B,2,200)],hours:3).Groups.Single();
        Assert.Equal(100,result.Members[0].Samples[2].Price);
        Assert.Equal(150,result.FusedSamples[2].Price);Assert.Equal(150,result.FusedSamples[2].Index);
        Assert.Equal(1,result.FusedSamples[2].ObservationCount);Assert.True(result.FusedSamples[2].IsCarried);
        Assert.Equal(150,result.FusedSamples[3].Price);Assert.Equal(0,result.FusedSamples[3].ObservationCount);
    }

    [Fact]
    public void ResetInvalidatesCarryUntilANewObservationArrives()
    {
        var result=Run([Group(A)],[Obs(A,0,10),Obs(A,1,0,clear:true),Obs(A,2,30)]).Groups.Single().FusedSamples;
        Assert.Null(result[1].Price); Assert.Equal(300,result[2].Index);
    }

    [Fact]
    public void ResetOnlyInvalidatesItsOwnSourceAndLot()
    {
        var definition=Group(A); definition.SourceMode=MarketAnalyticsSourceMode.All;
        var result=Run([definition],[Obs(A,0,10),Obs(A,0,20,source:MarketPriceSource.Manual),Obs(A,1,0,source:MarketPriceSource.Manual,clear:true)]).Groups.Single();
        Assert.Equal(15,result.Members[0].ReferencePrice); Assert.Equal(10,result.FusedSamples[1].Price);
    }

    [Fact]
    public void NonoverlappingPricesCannotInventACommonBase100Reference()
    {
        var result=Run([Group(A),Group(B)],[Obs(A,0,10),Obs(B,2,1000)],maxAge:1);
        Assert.Null(result.ReferenceUtc);
        Assert.All(result.Groups.SelectMany(g=>g.Members).SelectMany(m=>m.Samples),p=>Assert.Null(p.Index));
        Assert.Equal(10,result.Groups[0].Members[0].Samples[0].Price);
    }

    [Fact]
    public void FutureInvalidAndWrongSourceObservationsCannotEnterTheGraph()
    {
        var definition=Group(A);
        var result=Run([definition],[Obs(A,0,10),Obs(A,1,0),Obs(A,1,999,source:MarketPriceSource.Manual),Obs(A,1,999,lot:0),Obs(A,3,999)]).Groups.Single();
        Assert.All(result.FusedSamples,p=>Assert.Equal(10,p.Price));
    }

    [Fact]
    public void LookbackCanSeedTheCommonReferenceWithoutFabricatingNewReadings()
    {
        var result=Run([Group(A,B)],[Obs(A,-1,10),Obs(B,-2,1000),Obs(A,1,20)]);
        Assert.Equal(Start,result.ReferenceUtc); Assert.Equal(150,result.Groups.Single().FusedSamples[1].Index);
        Assert.Equal(0,result.Groups.Single().FusedSamples[0].ObservationCount);
    }

    [Fact]
    public void EmptyCatalogMembersShareOneTimelineAndLongPeriodsHaveBoundedPoints()
    {
        var members=Enumerable.Range(1,2000).Select(i=>new MarketAnalyticsMember(MarketObjectType.Rune,i,$"Rune {i}")).ToArray();
        var definition=Group(members);
        var result=Run([definition],[Obs(members[0],0,10)],hours:24*365*5).Groups.Single();
        Assert.InRange(result.Members[0].Samples.Count,2,160);
        Assert.Same(result.Members[1].Samples,result.Members[1999].Samples);
    }

    [Theory]
    [InlineData(MarketAnalyticsAggregation.Median,15)]
    [InlineData(MarketAnalyticsAggregation.Mean,32.5)]
    [InlineData(MarketAnalyticsAggregation.FirstQuartile,7.5)]
    [InlineData(MarketAnalyticsAggregation.ThirdQuartile,40)]
    public void AggregationsUseDefinedInterpolatedQuantiles(MarketAnalyticsAggregation aggregation,double expected)
    { Assert.Equal(expected,MarketAnalyticsCalculator.Aggregate([0,10,20,100],aggregation)); }
}
