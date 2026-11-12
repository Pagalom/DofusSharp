using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;

namespace Tests.BestCrush;

public sealed class SalesKineticsTest
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Fresh = TimeSpan.FromDays(2);
    private static readonly SalesMarketKey Market = new("Tylezia", MarketObjectType.Resource, 881);
    private static GlobalSalesObservation Observation(long units = 300, double days = 3,
        SalesMarketKey? market = null, DateTimeOffset? end = null, DateTimeOffset? captured = null)
    {
        var until = end ?? Now;
        return new(market ?? Market, until.AddDays(-days), until, units, captured ?? Now);
    }

    [Fact]
    public void DemandUsesSoldItemUnitsAndTheActualObservedPeriod()
    {
        var result = SalesKineticsCalculator.Evaluate(Market, 50, Observation(), Now, Fresh);
        Assert.Equal(SalesKineticsStatus.Available, result.Status);
        Assert.Equal(100d, result.UnitsPerDay);
        Assert.Equal(0.5d, result.MarketAbsorptionDays);
        var halfDay = SalesKineticsCalculator.Evaluate(Market, 50, Observation(100, 0.5), Now, Fresh);
        Assert.Equal(200d, halfDay.UnitsPerDay);
        Assert.Equal(0.25d, halfDay.MarketAbsorptionDays);
    }

    [Fact]
    public void MissingSalesPreserveKnownPricesWithoutInventingKinetics()
    {
        var result = SalesKineticsCalculator.EvaluateProfitability(Market, 10, 1500, 1000, null, Now, Fresh);
        Assert.Equal(500d, result.Gain);
        Assert.Equal(50d, result.RoiPercent);
        Assert.True(result.HasCompletePrices);
        Assert.False(result.CanCompareKinetics);
        Assert.Null(result.MarketDailyGainCapacity);
        Assert.Equal(SalesKineticsStatus.Missing, result.Kinetics.Status);
    }

    [Fact]
    public void UnknownStoneCostPreservesRevenueButCannotGenerateAProfit()
    {
        var result = SalesKineticsCalculator.EvaluateProfitability(Market, 1, 9000, null, Observation(), Now, Fresh);
        Assert.Equal(9000d, result.Revenue);
        Assert.Null(result.Cost);
        Assert.Null(result.Gain);
        Assert.Null(result.RoiPercent);
        Assert.Null(result.MarketDailyGainCapacity);
        Assert.False(result.CanCompareKinetics);
    }

    [Fact]
    public void AbsentRevenueCannotBeTurnedIntoZeroAndFreeCostHasNoInfiniteRoi()
    {
        var incomplete = SalesKineticsCalculator.EvaluateProfitability(Market, 1, null, 100, Observation(), Now, Fresh);
        Assert.Null(incomplete.Gain);
        var free = SalesKineticsCalculator.EvaluateProfitability(Market, 1, 900, 0, Observation(), Now, Fresh);
        Assert.Equal(900d, free.Gain);
        Assert.Null(free.RoiPercent);
    }

    [Fact]
    public void EqualMarginsHaveDifferentMarketCapacityWhenDemandDiffers()
    {
        var fast = SalesKineticsCalculator.EvaluateProfitability(Market, 100, 2000, 1000, Observation(300), Now, Fresh);
        var slow = SalesKineticsCalculator.EvaluateProfitability(Market, 100, 2000, 1000, Observation(30), Now, Fresh);
        Assert.Equal(fast.Gain, slow.Gain);
        Assert.Equal(1000d, fast.MarketDailyGainCapacity);
        Assert.Equal(100d, slow.MarketDailyGainCapacity);
    }

    [Fact]
    public void KnownZeroSalesAreDistinctFromMissingHistoryAndNeverDivideByZero()
    {
        var result = SalesKineticsCalculator.EvaluateProfitability(Market, 100, 2000, 1000, Observation(0), Now, Fresh);
        Assert.Equal(SalesKineticsStatus.NoObservedSales, result.Kinetics.Status);
        Assert.Equal(0d, result.Kinetics.UnitsPerDay);
        Assert.Null(result.Kinetics.MarketAbsorptionDays);
        Assert.Null(result.MarketDailyGainCapacity);
        Assert.False(result.CanCompareKinetics);
        Assert.Equal(1000d, result.Gain);
    }

    [Fact]
    public void ServerTypeAndCapturedVariantMustMatchExactly()
    {
        Assert.Equal(Market, new SalesMarketKey(" tylezia ", MarketObjectType.Resource, 881));
        SalesMarketKey[] foreign = [new("Kourial", MarketObjectType.Resource, 881),
            new("Tylezia", MarketObjectType.Equipment, 881), new("Tylezia", MarketObjectType.Resource, 882),
            new("Tylezia", MarketObjectType.Resource, 881, "boss:147")];
        foreach (var key in foreign)
            Assert.Equal(SalesKineticsStatus.Missing, SalesKineticsCalculator.Evaluate(Market, 1, Observation(market: key), Now, Fresh).Status);
    }

    [Fact]
    public void ReopeningAnOldPeriodDoesNotMakeItsDemandCurrent()
    {
        var old = Observation(end: Now.AddDays(-10), captured: Now);
        var result = SalesKineticsCalculator.Evaluate(Market, 100, old, Now, Fresh);
        Assert.Equal(SalesKineticsStatus.Outdated, result.Status);
        Assert.Null(result.UnitsPerDay);
        Assert.Null(result.MarketAbsorptionDays);
        Assert.NotNull(result.Evidence);
        var future = Observation(end: Now.AddHours(1), captured: Now.AddHours(1));
        Assert.Equal(SalesKineticsStatus.Outdated, SalesKineticsCalculator.Evaluate(Market, 1, future, Now, Fresh).Status);
    }

    [Fact]
    public void InvalidCountsPeriodsAndAmountsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Observation(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlobalSalesObservation(Market, Now, Now, 1, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => Observation(captured: Now.AddHours(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SalesMarketKey("Tylezia", MarketObjectType.Resource, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SalesKineticsCalculator.Evaluate(Market, 0, null, Now, Fresh));
        Assert.Throws<ArgumentOutOfRangeException>(() => SalesKineticsCalculator.Evaluate(Market, 1, null, Now, TimeSpan.Zero));
        foreach (double amount in new[] { double.NaN, double.PositiveInfinity, -1d })
            Assert.Throws<ArgumentOutOfRangeException>(() => SalesKineticsCalculator.EvaluateProfitability(Market, 1, amount, 0, null, Now, Fresh));
    }

    [Fact]
    public void LossesRemainNegativeAndExtremeResultsDoNotExposeNonFiniteNumbers()
    {
        var loss = SalesKineticsCalculator.EvaluateProfitability(Market, 1, 10, 100, Observation(), Now, Fresh);
        Assert.Equal(-90d, loss.Gain);
        Assert.Equal(-90d, loss.RoiPercent);
        var extreme = SalesKineticsCalculator.EvaluateProfitability(Market, 1, double.MaxValue, 1, Observation(), Now, Fresh);
        Assert.Null(extreme.MarketDailyGainCapacity);
        Assert.Null(extreme.RoiPercent);
    }

    [Fact]
    public void RepositoryDeduplicatesPeriodsRejectsOlderCorrectionsAndSurvivesReopen()
    {
        string path = Path.Combine(Path.GetTempPath(), "sales-kinetics-tests", Guid.NewGuid() + ".db");
        try
        {
            var repository = new GlobalSalesRepository(path);
            var first = Observation(captured: Now.AddHours(-1), end: Now.AddHours(-1));
            Assert.True(repository.Save(first));
            Assert.False(repository.Save(first));
            var corrected = new GlobalSalesObservation(Market, first.PeriodStartUtc, first.PeriodEndUtc, 600, Now);
            Assert.True(repository.Save(corrected));
            Assert.False(repository.Save(first));
            Assert.Equal(corrected, new GlobalSalesRepository(path).ReadLatest(Market, Now));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void RepositoryDoesNotSumOverlappingWindowsOrLeakServersAndSoulVariants()
    {
        string path = Path.Combine(Path.GetTempPath(), "sales-kinetics-tests", Guid.NewGuid() + ".db");
        try
        {
            var repository = new GlobalSalesRepository(path);
            repository.Save(Observation(300, 3));
            repository.Save(Observation(3000, 30));
            var future = Observation(9000, 30, end: Now.AddDays(1), captured: Now.AddDays(1));
            repository.Save(future);
            Assert.Equal(3000, repository.ReadLatest(Market, Now)!.UnitsSold);
            Assert.Null(repository.ReadLatest(new("Kourial", MarketObjectType.Resource, 881), Now));
            var bossA = new SalesMarketKey("Tylezia", MarketObjectType.Resource, 7010, "boss:147;variant:a");
            var bossB = new SalesMarketKey("Tylezia", MarketObjectType.Resource, 7010, "boss:58;variant:a");
            repository.Save(Observation(5, 3, bossA));
            Assert.Equal(5, repository.ReadLatest(bossA, Now)!.UnitsSold);
            Assert.Null(repository.ReadLatest(bossB, Now));
            Assert.Null(repository.ReadLatest(new("Tylezia", MarketObjectType.Resource, 7010), Now));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
