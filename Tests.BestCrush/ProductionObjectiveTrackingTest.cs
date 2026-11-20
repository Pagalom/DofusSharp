using System.Numerics;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush;

public sealed class ProductionObjectiveTrackingTest
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly PetSpecies Species = new(100, "Familier", null, 18);
    private static PetAssessment Pet(ulong? uid = null, int level = 0, string variant = "normal", int target = 80)
    {
        long xp = PetExperience.RequiredAtLevel(level);
        var nutrition = new PetNutritionPlan([], [], [], 0, 0, new Dictionary<long, int>());
        var plan = new PetSalePlan(target, 10000, nutrition, 9000, 5, true);
        return new(Species, uid is { } id ? new(Species, id, 1, "inventory", new(level, xp, variant, false, false)) : null,
            uid is null ? new(level, xp, 100, variant, false) : null, 100, [plan], plan, null);
    }
    private static PetOpportunityReport Report(PetAssessment[]? owned = null, PetAssessment[]? purchases = null) =>
        new("A", owned ?? [], purchases ?? [], [], new Dictionary<long, BigInteger>(), new Dictionary<long, BigInteger>(),
            true, true, true, true, 0, 0);

    [Fact]
    public void PurchaseFoodExperienceAndListingAreAutomaticAndPersistedWithoutCountingTwice()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var journal = new PetObjectiveJournal(); var purchase = Pet();
            PetObjectiveProgressStore.Observe(journal, Report(purchases: [purchase]), [], Now);
            var goal = Assert.Single(journal.Goals).Value;
            Assert.Equal(0, goal.Percent);
            PetObjectiveProgressStore.Observe(journal, Report([Pet(1)], [purchase]), [], Now.AddMinutes(1));
            Assert.Equal(40, goal.Percent); Assert.Equal(1UL, goal.Uid); Assert.Single(journal.Goals);
            PetObjectiveProgressStore.Observe(journal, Report([Pet(1, 60)], [purchase]), [], Now.AddMinutes(2));
            Assert.InRange(goal.Percent, 41, 89);
            new PetObjectiveProgressStore(path).Save("player-a", journal);
            journal = new PetObjectiveProgressStore(path).Read("player-a"); goal = Assert.Single(journal.Goals).Value;
            Assert.Empty(new PetObjectiveProgressStore(path).Read("player-b").Goals);
            PetObjectiveProgressStore.Observe(journal, Report([Pet(1, 80)], [purchase]), [], Now.AddMinutes(3));
            Assert.Equal(90, goal.Percent);
            var listing = new ProductionListing("sale", "player-a", 100, 1, 80, "normal", null, Now.AddMinutes(4));
            PetObjectiveProgressStore.Observe(journal, Report(purchases: [purchase]), [listing], Now.AddMinutes(4));
            Assert.Equal(0, goal.Cycles); Assert.Equal(95, goal.Percent); Assert.Equal(1UL, goal.Uid);
            PetObjectiveProgressStore.Observe(journal, Report(purchases: [purchase]), [listing], Now.AddMinutes(5));
            Assert.Equal(0, goal.Cycles);
            PetObjectiveProgressStore.Observe(journal, Report([Pet(2)], [purchase]), [listing], Now.AddMinutes(6));
            Assert.Equal(1UL, goal.Uid); Assert.Equal(95, goal.Percent);
            Assert.Contains(journal.Goals.Values, g => g.Uid == 2 && g.Percent == 40);
            var sale = new ProductionSale("receipt", "sale", Now.AddMinutes(7));
            PetObjectiveProgressStore.Observe(journal, Report([Pet(2)], [purchase]), [listing], Now.AddMinutes(7), [sale]);
            Assert.Equal(1, goal.Cycles); Assert.Equal(100, goal.Percent);
            PetObjectiveProgressStore.Observe(journal, Report([Pet(2)], [purchase]), [listing], Now.AddMinutes(8), [sale]);
            Assert.Equal(1, goal.Cycles);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExistingPetsRemainOwnedAndCannotBeMistakenForNewPurchases()
    {
        var journal = new PetObjectiveJournal();
        PetObjectiveProgressStore.Observe(journal, Report([Pet(1)], [Pet()]), [], Now);
        var purchase = journal.Goals.Values.Single(g => g.Purchase);
        Assert.Null(purchase.Uid);
        Assert.Equal(1UL, journal.Goals.Values.Single(g => !g.Purchase).Uid);
        PetObjectiveProgressStore.Observe(journal, Report([Pet(1), Pet(2)], [Pet()]), [], Now.AddMinutes(1));
        Assert.Equal(2UL, purchase.Uid);
    }

    [Fact]
    public void IdenticalStackedPetsCanBePreparedAndSoldInParallelAndEachReceiptCountsOnce()
    {
        var journal = new PetObjectiveJournal(); var pet = Pet(42);
        pet = pet with { Owned = pet.Owned! with { Quantity = 2 } };
        PetObjectiveProgressStore.Observe(journal, Report([pet]), [], Now);
        var listings = new[] { new ProductionListing("one", "a", 100, 42, 80, "normal", null, Now.AddMinutes(1)),
            new("two", "a", 100, 42, 80, "normal", null, Now.AddMinutes(2)) };
        PetObjectiveProgressStore.Observe(journal, Report(), listings, Now.AddMinutes(3));
        var goal = Assert.Single(journal.Goals).Value;
        Assert.Equal(2, goal.PendingListings.Count); Assert.Equal(95, goal.Percent); Assert.Equal(0, goal.Cycles);
        var first = new ProductionSale("receipt-one", "one", Now.AddMinutes(4));
        PetObjectiveProgressStore.Observe(journal, Report(), listings, Now.AddMinutes(4), [first]);
        Assert.Single(goal.PendingListings); Assert.Equal(1, goal.Cycles); Assert.Equal(95, goal.Percent);
        var second = new ProductionSale("receipt-two", "two", Now.AddMinutes(5));
        PetObjectiveProgressStore.Observe(journal, Report(), listings, Now.AddMinutes(5), [first, second]);
        Assert.Empty(goal.PendingListings); Assert.Equal(2, goal.Cycles); Assert.Equal(100, goal.Percent);
    }

    [Fact]
    public void ReturnedPetCanBeRelistedWithoutCompletingItsCycle()
    {
        var journal = new PetObjectiveJournal(); PetObjectiveProgressStore.Observe(journal, Report([Pet(42)]), [], Now);
        var listing = new ProductionListing("one", "a", 100, 42, 80, "normal", null, Now.AddMinutes(1));
        PetObjectiveProgressStore.Observe(journal, Report(), [listing], Now.AddMinutes(1));
        var returned = new ProductionReturn("returned", "one", Now.AddMinutes(2));
        PetObjectiveProgressStore.Observe(journal, Report(), [listing], Now.AddMinutes(2), returns: [returned]);
        var goal = Assert.Single(journal.Goals).Value;
        Assert.Equal(0, goal.Cycles); Assert.Empty(goal.PendingListings); Assert.Equal(90, goal.Percent); Assert.Null(goal.ListingId);
    }

    [Fact]
    public void PetTargetAdaptsAfterThePlayerFeedsPastItsInitialTarget()
    {
        var journal = new PetObjectiveJournal(); PetObjectiveProgressStore.Observe(journal, Report([Pet(42)]), [], Now);
        PetObjectiveProgressStore.Observe(journal, Report([Pet(42, 81, target: 90)]), [], Now.AddMinutes(1));
        Assert.Equal(90, Assert.Single(journal.Goals).Value.TargetLevel);
    }

    [Fact]
    public void SavedBreedingObjectiveStillRecognizesItsListingWhenItsMarketOpportunityDisappears()
    {
        var key = "100:80:True"; var journal = new BreedingObjectiveJournal();
        journal.Goals[key] = new() { Steps = [new(BreedingObjectiveAction.Sell, "A", Level: 80)], StartedAt = Now };
        var listing = new ProductionListing("listing", "scope", 100, 42, 80, "mount", true, Now.AddMinutes(1));
        Assert.True(BreedingObjectiveProgressStore.ObserveListings(journal, [], new Dictionary<string, IReadOnlyList<BreedingObjectiveStep>>(), [listing], Now.AddMinutes(1)));
        Assert.Equal(95, BreedingObjectiveProgressStore.Percent(journal.Goals[key]));
    }

    [Fact]
    public void MissingStockAndWrongLevelOrPowerNeverProveAListing()
    {
        var journal = new PetObjectiveJournal();
        PetObjectiveProgressStore.Observe(journal, Report([Pet(1)]), [], Now);
        var goal = Assert.Single(journal.Goals).Value;
        foreach (var listing in new[] { new ProductionListing("wrong-level", "a", 100, 1, 70, "normal", null, Now.AddMinutes(1)),
            new("wrong-power", "a", 100, 1, 80, "legendary", null, Now.AddMinutes(1)) })
            PetObjectiveProgressStore.Observe(journal, Report(), [listing], Now.AddMinutes(2));
        Assert.Equal(0, goal.Cycles); Assert.Equal(40, goal.Percent);
    }

    [Fact]
    public void DamagedPetJournalIsPreserved()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, "{broken");
            Assert.Throws<InvalidDataException>(() => new PetObjectiveProgressStore(path).Save("a", new()));
            Assert.Equal("{broken", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BreedingFollowsBothParentsAndPartialGaugesEvenWhenTheFirstParentIsNotReady()
    {
        var female = new PaddockMountReading(Guid.NewGuid(), 1, 0, 0, 10000)
        { SpeciesId = 1, Sex = BreedingMountSex.Female, Fertility = BreedingMountFertility.Fertile, Endurance = 10000, Love = 10000 };
        var male = female with { Id = Guid.NewGuid(), Sex = BreedingMountSex.Male, SpeciesId = 2 };
        var progress = new BreedingObjectiveProgress { Steps = [
            new(BreedingObjectiveAction.Capture, "A", Sex: BreedingMountSex.Female) { RideId = 1 },
            new(BreedingObjectiveAction.Prepare, "A", Sex: BreedingMountSex.Female, Level: 1) { RideId = 1 },
            new(BreedingObjectiveAction.Buy, "B", Sex: BreedingMountSex.Male) { RideId = 2 },
            new(BreedingObjectiveAction.Prepare, "B", Sex: BreedingMountSex.Male, Level: 1) { RideId = 2 }] };
        BreedingObjectiveProgressStore.Observe(progress, [female, male]);
        Assert.Equal(75, BreedingObjectiveProgressStore.Percent(progress));
        Assert.Equal(new[] { 0, 2 }, progress.Done.Order().ToArray());
        BreedingObjectiveProgressStore.Observe(progress, [female with { Maturity = 15000, Love = 15000, Endurance = 15000 }, male]);
        Assert.True(BreedingObjectiveProgressStore.Percent(progress) > 75);
        Assert.Equal(female.Id, progress.Steps[1].OwnedId); Assert.Equal(male.Id, progress.Steps[3].OwnedId);
    }

    [Theory]
    [InlineData(1, 20)] [InlineData(99, 20)] [InlineData(100, 40)] [InlineData(149, 40)]
    [InlineData(150, 60)] [InlineData(199, 60)] [InlineData(200, 120)]
    public void CaptureDefaultsFollowProfessionMilestones(int level, int expected) =>
        Assert.Equal(expected, BreedingPlanningOptions.DefaultCaptures(level));

    [Fact]
    public void CaptureBudgetChangesPriorityInsteadOfTreatingTheMarketQuotaAsProductionCapacity()
    {
        var sale = new BreedingResaleOption(1, 10000, 0, Now);
        var market = new BreedingMarketCapacity(10000, 10000, 5000, 158, 5000, Now);
        var slow = new BreedingMarketObjective(1, 101, 10, "Lente", sale,
            new(1, BreedingSupplyKind.Cross, 0) { ExpectedCaptures = 100 }, market) { CapturesPerDay = 20 };
        var fast = slow with { Production = new(2, BreedingSupplyKind.Capture, 8000) { ExpectedCaptures = 1 } };
        Assert.True(slow.WeeklyPotential < fast.WeeklyPotential);
        Assert.Equal(5m, slow.CaptureDays); Assert.Equal(0, slow.Today); Assert.Equal(20, fast.Today);
        Assert.True((slow with { CapturesPerDay = 1000 }).WeeklyPotential > fast.WeeklyPotential);
        Assert.Equal(158, market.Remaining);
        Assert.Equal(2, (fast with { BudgetKamas = 16000 }).Today);
        Assert.Equal(0, (fast with { BudgetKamas = 100 }).Today);
        var bought = fast with { Production = new(2, BreedingSupplyKind.Purchase, 1000), BudgetKamas = 500000, CapturesPerDay = 1 };
        Assert.Equal(158, bought.Today); // No capture/farming throttle on purchases.
    }

    [Fact]
    public void ConfirmedListingWithTheExpectedCharacteristicsClosesOneBreedingCycleEvenAtQuotaZero()
    {
        var goal = new BreedingMarketObjective(1, 100, 1, "A", new(80, 10000, 0, Now, Fertile: true),
            new(1, BreedingSupplyKind.Capture, 0), new(100, 100, 50, 0, 50, Now));
        string key = BreedingObjectiveProgressStore.Key(goal);
        var step = new BreedingObjectiveStep(BreedingObjectiveAction.Sell, "A", Level: 80) { RideId = 1 };
        var journal = new BreedingObjectiveJournal(); journal.Goals[key] = new() { Steps = [step], StartedAt = Now };
        var plans = new Dictionary<string, IReadOnlyList<BreedingObjectiveStep>> { [key] = [step] };
        var listing = new ProductionListing("listing", "scope", 100, 42, 80, "mount", true, Now.AddMinutes(1));
        Assert.True(BreedingObjectiveProgressStore.ObserveListings(journal, [goal], plans, [listing], Now.AddMinutes(1)));
        Assert.Equal(0, journal.Goals[key].Cycles); Assert.Empty(journal.Pending);
        Assert.Equal(95, BreedingObjectiveProgressStore.Percent(journal.Goals[key]));
        Assert.False(BreedingObjectiveProgressStore.ObserveListings(journal, [goal], plans, [listing], Now.AddMinutes(2)));
        var receipt = new ProductionSale("receipt", "listing", Now.AddMinutes(3));
        Assert.True(BreedingObjectiveProgressStore.ObserveListings(journal, [goal], plans, [listing], Now.AddMinutes(3), [receipt]));
        Assert.Equal(1, journal.Goals[key].Cycles); Assert.Equal(0, BreedingObjectiveProgressStore.Percent(journal.Goals[key]));
        Assert.False(BreedingObjectiveProgressStore.ObserveListings(journal, [goal], plans, [listing], Now.AddMinutes(4), [receipt]));
    }

    [Fact]
    public void ListingEvidenceReadsPetPowersAndMountFertilityAndSurvivesRepositoryRestart()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            var effects = new[] { Message(Unsigned(10, 2821), Bytes(9, Message(Unsigned(1, 18), Unsigned(4, 80)))),
                Message(Unsigned(10, 2819), Bytes(9, Unsigned(4, (ulong)PetExperience.RequiredAtLevel(80)))) };
            var item = new LiveStockItem(100, 42, 1, null, effects, null);
            var evidence = ProductionListingReader.FromStock("a", item)!;
            Assert.Equal(80, evidence.Level); Assert.Equal("normal", evidence.Variant); Assert.Null(evidence.Fertile);
            var repository = new GlobalSalesRepository(path);
            Assert.True(repository.SaveListing("server", new(123, 100, 1, 10000, []), Now, evidence));
            Assert.False(repository.SaveListing("server", new(123, 100, 1, 10000, []), Now, evidence));
            var read = new GlobalSalesRepository(path).ReadProductionListings("server", "a", Now);
            Assert.Equal(42UL, Assert.Single(read).InstanceUid);
            Assert.Empty(repository.ReadProductionListings("server", "b", Now));
            Assert.Empty(repository.ReadProductionListings("other-server", "a", Now));
            Assert.Empty(repository.ReadProductionListings("server", "a", Now.AddSeconds(-1)));
            var receipt = new ProductionSale("receipt", "123", Now.AddMinutes(1));
            Assert.False(repository.SaveProductionSale("server", "b", receipt));
            Assert.False(repository.SaveProductionSale("server", "a", receipt with { ListingId = "unknown" }));
            Assert.True(repository.SaveProductionSale("server", "a", receipt));
            Assert.False(repository.SaveProductionSale("server", "a", receipt));
            Assert.Empty(repository.ReadProductionSales("server", "a", Now));
            Assert.Single(new GlobalSalesRepository(path).ReadProductionSales("server", "a", Now.AddMinutes(1)));
        }
        finally { File.Delete(path); }
    }
}
