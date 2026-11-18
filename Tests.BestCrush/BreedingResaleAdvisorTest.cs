using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using Tests.BestCrush.Utils;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush;

public sealed class BreedingResaleAdvisorTest
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReadsTheDisplayedLevelAndFertileMarkerOfActualOfferShapes()
    {
        var body = Message(Unsigned(2, 331), Unsigned(3, 33001),
            Offer(123, 1, 5_998, true), Offer(124, 100, 95_000, false), Offer(125, 200, 120_000, true));
        var result = BreedingMarketDecoder.Decode(body);
        Assert.NotNull(result);
        Assert.Equal(33001, result.ItemId);
        Assert.Equal(
            [new(1, 5_998, true), new(100, 95_000, false), new(200, 120_000, true)],
            result.Offers);
        Assert.Null(BreedingMarketDecoder.Decode(Message(Unsigned(2, 331), Unsigned(3, 33001))));
    }

    [Fact]
    public void SeparatesServerLevelAndFertilityAndKeepsTheCheapestCurrentOffer()
    {
        string path = Path.Combine(Path.GetTempPath(), $"breeding-market-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new GlobalSalesRepository(path);
            var changes = new List<string>();
            repository.BreedingPricesChanged += server =>
            {
                Assert.NotEmpty(repository.ReadBreedingPrices(server, Now.AddDays(-1), Now));
                changes.Add(server);
            };
            repository.SaveBreedingPrices("Kourial", new(33001,
                [new(1, 6_000, true), new(1, 5_998, true), new(1, 4_000, false), new(100, 95_000, true)]), Now);
            repository.SaveBreedingPrices("Kourial", new(33001, [new(1, 2_000, true)]), Now.AddMinutes(-1));
            repository.SaveBreedingPrices("Imagiro", new(33001, [new(100, 110_000, true)]), Now);
            Assert.Equal(["KOURIAL", "IMAGIRO"], changes);
            var prices = repository.ReadBreedingPrices("kourial", Now.AddDays(-7), Now);
            Assert.Equal(3, prices.Count);
            Assert.Equal(5_998, Assert.Single(prices, quote => quote.Level == 1 && quote.Fertile).Price);
            Assert.Equal(6_000, Assert.Single(prices, quote => quote.Level == 1 && quote.Fertile).ThirdQuartile);
            Assert.Equal(95_000, Assert.Single(prices, quote => quote.Level == 100).Price);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void UsesOnlyTheLatestReleveForQuartilesAndKeepsOldLevelsAsReferences()
    {
        string path = Path.Combine(Path.GetTempPath(), $"breeding-quartiles-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new GlobalSalesRepository(path);
            repository.SaveBreedingPrices("Kourial", new(33001,
                [new(1, 10_000, true), new(100, 80_000, true)]), Now.AddDays(-1));
            repository.SaveBreedingPrices("Kourial", new(33001,
                [new(1, 10_000, true), new(1, 20_000, true), new(1, 30_000, true),
                  new(1, 50_000, true), new(1, 5_000, false)]), Now);
            var latest = repository.ReadBreedingPrices("Kourial", Now.AddDays(-5), Now);
            var current = Assert.Single(latest, price => price is { Level: 1, Fertile: true });
            Assert.Equal(35_000, current.ThirdQuartile);
            Assert.Equal(4, current.OfferCount);
            Assert.Equal(9_999, current.SuggestedPrice);
            Assert.False(BreedingResaleAdvisor.IsFresh(current, Now.AddDays(4)));
            Assert.Null(Assert.Single(latest, price => price.Level == 100).ThirdQuartile);
            Assert.DoesNotContain(BreedingResaleAdvisor.LatestSnapshot(latest), price => price.Level == 100);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void CaptureCanPreferTheNextHigherGenerationWithAnOwnedPartner()
    {
        var mount = new PaddockMountReading(Guid.NewGuid(), 100, 172_668, 0, 20_000)
            { SpeciesId = 18, Sex = BreedingMountSex.Male, Fertility = BreedingMountFertility.Ready };
        var partner = new BreedingPairParent(mount, "Dorée", 1, new(100, 20_000, 0, Now),
            new(33011, 100, 3_501, false, Now));
        var directCross = Assert.Single(BreedingPortfolioAdvisor.CaptureCrosses(BreedingLineage.Rides[20], [partner]));
        var prices = new Dictionary<string, BreedingResaleOption>
        {
            ["Amande"] = new(1, 10_000, 0, Now),
            ["Dorée"] = new(1, 14_000, 0, Now),
            ["Amande et Dorée"] = new(80, 130_000, 10_000, Now)
        };
        var fees = new Dictionary<Guid, long?> { [Guid.Empty] = 6_000, [mount.Id] = 4_000 };
        var names = new Dictionary<long, string> { [33002] = "Amande et Dorée" };
        var cross = BreedingPortfolioAdvisor.EstimateCaptureCross(directCross, "Amande",
            new(33001, 1, 1_501, false, Now), fees, names, prices);
        Assert.NotNull(cross);
        Assert.Equal(2, cross.Generation);
        Assert.Equal(34_665m, cross.Net);
        Assert.True(new BreedingCaptureOption("Amande", "Territoire", prices["Amande"], 0, cross).PreferCross);
        Assert.Null(BreedingPortfolioAdvisor.EstimateCaptureCross(directCross, "Amande", null, fees, names, prices));
    }

    [Fact]
    public void RanksParentsUsingTheGameLineageAndChargesTheirLostValue()
    {
        var first = new PaddockMountReading(Guid.NewGuid(), 1, 0, -300, 20_000)
            { SpeciesId = 20, Sex = BreedingMountSex.Female, Fertility = BreedingMountFertility.Ready };
        var second = first with { Id = Guid.NewGuid(), SpeciesId = 18, Sex = BreedingMountSex.Male };
        var cross = Assert.Single(BreedingLineage.DirectCrosses(18, 20));
        Assert.Equal(2, cross.Generation);
        Assert.Equal(33002, cross.ItemId);

        var offspring = new BreedingResaleOption(50, 45_000, 5_000, Now, 4);
        var sterile = new BreedingLevelPrice(33001, 1, 6_001, false, Now, 6_001, 4);
        var sale = new BreedingResaleOption(1, 12_000, 0, Now, 4);
        var pair = Assert.Single(BreedingPortfolioAdvisor.PairOptions(
            [new(first, "Amande", 1, sale, sterile), new(second, "Dorée", 1, sale, sterile)],
            new Dictionary<string, BreedingResaleOption> { ["Amande et Dorée"] = offspring },
            new Dictionary<long, string> { [33002] = "Amande et Dorée" }));
        Assert.Equal(12_000m, pair.FertilityCost);
        Assert.Equal(26_000m, BreedingPortfolioAdvisor.ConfirmedGain(pair, [(offspring, 100m)], 2_000));
        Assert.Null(BreedingPortfolioAdvisor.ConfirmedGain(pair, [(offspring, 80m), (null, 20m)], 0));
    }

    [Fact]
    public void PreparingAFertileParentUsesTheCorrectSerenityAndGaugeFuels()
    {
        var mount = new PaddockMountReading(Guid.NewGuid(), 1, 0, 5_000, 18_000)
            { Endurance = 19_000, Love = 20_000, Fertility = BreedingMountFertility.Fertile };
        var demands = BreedingPortfolioAdvisor.PreparationNeeds(mount)!;
        Assert.Equal(5_001, demands[PaddockGauge.SerenityLoss]);
        Assert.Equal(1_000, demands[PaddockGauge.Endurance]);
        Assert.Equal(2_000, demands[PaddockGauge.Maturity]);
        Assert.False(demands.ContainsKey(PaddockGauge.Love));
    }

    [Fact]
    public void UsesAnIntermediateLevelWhenItsNetReturnBeatsTheMaxLevel()
    {
        var mount = new PaddockMountReading(Guid.NewGuid(), 32, 13_000, 0, 0);
        var quotes = new BreedingLevelPrice[]
        {
            new(33001, 1, 5000, true, Now),
            new(33001, 32, 10_000, true, Now),
            new(33001, 100, 95_000, true, Now),
            new(33001, 200, 120_000, true, Now)
        };
        var costs = new Dictionary<int, long> { [159_668] = 20_000, [854_582] = 65_000 };
        var result = BreedingResaleAdvisor.Compare(mount, quotes, costs);
        Assert.Equal(100, result.RecommendedLevel);
        Assert.Equal(74_999, result.Best!.Net);
        Assert.Equal(3, result.QuotedLevels);

        Assert.Null(BreedingResaleAdvisor.Compare(mount, quotes.Where(quote => quote.Level > 32).ToArray(), costs).RecommendedLevel);
        Assert.Equal(32, BreedingResaleAdvisor.Compare(mount, quotes,
            new Dictionary<int, long> { [159_668] = 85_000, [854_582] = 110_000 }).Best!.Level);
    }

    [Fact]
    public void RecognizesARelevéEvenWhenNoOfferMatchesTheMountsLevelAndFertility()
    {
        var mount = new PaddockMountReading(Guid.NewGuid(), 106, 198_000, 0, 20_000)
            { Fertility = BreedingMountFertility.Ready };
        var report = BreedingResaleAdvisor.Compare(mount,
            [new(33001, 100, 72_000, true, Now), new(33001, 200, 95_000, false, Now)], new Dictionary<int, long>());
        Assert.Equal(0, report.QuotedLevels);
        Assert.Null(report.Best);
        Assert.Equal(Now, report.LatestSpeciesQuoteAt);
    }

    [Fact]
    public void TrainingForecastOnlyIncludesTheMountsWithAResaleTarget()
    {
        var targeted = new PaddockMountReading(Guid.NewGuid(), 1, 0, 500, 0);
        var other = targeted with { Id = Guid.NewGuid() };
        var reading = new PaddockReading([new(PaddockGauge.Experience, true, 10_000)], [targeted, other]);
        var goals = new Dictionary<Guid, int> { [targeted.Id] = 5 };
        var plan = BreedingTrainingPlan.From(reading, PaddockGauge.Experience, resaleGoals: goals);
        Assert.Equal(targeted.Id, Assert.Single(plan.Mounts).Key);
        Assert.Equal(161, BreedingTrainingPlan.RefillTarget(reading, PaddockGauge.Experience, resaleGoals: goals));
    }

    [Fact]
    public void SterileMountDoesNotGenerateBreedingDemandOrACompatiblePair()
    {
        var first = new PaddockMountReading(Guid.NewGuid(), 100, 172_668, 0, 20_000)
            { Sex = BreedingMountSex.Female, Fertility = BreedingMountFertility.Ready };
        var second = new PaddockMountReading(Guid.NewGuid(), 1, 0, 0, 0)
            { Sex = BreedingMountSex.Male, Fertility = BreedingMountFertility.Sterile };
        Assert.False(BreedingDecisions.CanPair(first, second));
        Assert.Equal(0, BreedingTrainingRules.PointsNeeded(second, PaddockGauge.Maturity, 100));
        Assert.True(BreedingDecisions.CanPair(first, second with { Fertility = BreedingMountFertility.Ready }));
    }

    [Fact]
    public void PricesXpInWholeLotsAcrossRefillsAndIgnoresQuotesForOtherItems()
    {
        var fuel = new BreedingFuelItem(10, "Extrait d’XP", 2_000, 80_000, PaddockGauge.Experience);
        var market = new EquipmentProfitabilityContext
        {
            ResourcePrices = new Dictionary<(long, int), MarketPriceObservation>
            {
                [(10,1)] = Quote(10, 1, 40),
                [(10,10)] = Quote(10, 10, 200),
                [(99,1)] = Quote(99, 1, 1)
            },
            EquipmentPrices = new Dictionary<(long, int), MarketPriceObservation>(),
            RunePrices = new Dictionary<(long, int), MarketPriceObservation>(),
            Coefficients = []
        };
        var result = BreedingExperienceCost.Estimate([0, 2001, 18_000, 88_000], [fuel], [], null, market,
            new(new(null!, new TestDataPriorityProvider())), CancellationToken.None);
        Assert.Equal(0, result[0]);
        Assert.Equal(80, result[2001]);
        Assert.Equal(200, result[18_000]);
        Assert.Equal(960, result[88_000]);
    }

    private static MarketPriceObservation Quote(long id, int quantity, long price) => new()
    {
        ObjectType = MarketObjectType.Resource, DofusDbId = id, ServerName = "Kourial",
        Quantity = quantity, Price = price, ObservedAtUtc = Now.UtcDateTime, Source = default
    };

    private static byte[] Offer(ulong id, ulong level, ulong price, bool fertile) => Bytes(1, Message(
        Unsigned(1, 33001), Bytes(2, Packed(price, 0, 0, 0)), Unsigned(3, id), Unsigned(4, 331),
        Bytes(5, Message(Bytes(9, Message(Unsigned(1, 331), Unsigned(4, level))), Unsigned(10, 2821))),
        fertile ? Bytes(5, Message(Unsigned(5, 0), Unsigned(10, 3830))) : []));
}
