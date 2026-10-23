using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Tests.BestCrush.Utils;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class DofusNetworkRoutingTest
{
    private static readonly DateTime ObservedAt =
        new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private static byte[] CandidateRow(ulong id, string name, ulong level = 50) =>
        Message(Unsigned(3, id), Bytes(4, Message(
            Unsigned(3, level),
            Bytes(6, System.Text.Encoding.UTF8.GetBytes(name)))));

    private static byte[] CandidateRoster(params (ulong Id, string Name)[] rows) =>
        Message(rows.Select(row =>
            Bytes(2, CandidateRow(row.Id, row.Name))).ToArray());

    private static byte[] CandidateSelected(ulong id, string name) =>
        Bytes(1, Bytes(1, CandidateRow(id, name)));

    [Fact]
    public async Task OptInRosterObserverReceivesRealWorkerReplayButCannotWriteAccounts()
    {
        CurrentServerState manualServer = new();
        // The observer must reference the SAME server state as the worker.
        // Harness creates the state, so use its supplied observer factory.
        await using RoutingHarness harness = await RoutingHarness.CreateAsync(
            (state, workspace) => new EphemeralCandidateRosterWireObserver(state, workspace));

        EphemeralCandidateRosterGroupsState workspace = harness.RosterWorkspace!;
        DofusClientWindowKey a = new(101, 1201);
        DofusClientWindowKey b = new(102, 1202);
        long epoch = harness.Service.CaptureEpochForReplay;
        NetworkCaptureLease lease = harness.ServerState.GetCaptureLease()!.Value;

        await harness.Service.ReplayMessageAsync("S→C", "ksc",
            CandidateRoster((101, "Sable-Ciel"), (102, "Roche-Lune")),
            ObservedAt, connectionId: 11, candidateWindow: a);
        await harness.Service.ReplayMessageAsync("S→C", "ksb",
            CandidateSelected(101, "Sable-Ciel"),
            ObservedAt.AddSeconds(1), connectionId: 11, candidateWindow: a);
        await harness.Service.ReplayMessageAsync("S→C", "ksc",
            CandidateRoster((201, "Hiver-Rouge")),
            ObservedAt.AddSeconds(2), connectionId: 22, candidateWindow: b);
        await harness.Service.ReplayMessageAsync("S→C", "ksb",
            CandidateSelected(201, "Hiver-Rouge"),
            ObservedAt.AddSeconds(3), connectionId: 22, candidateWindow: b);

        workspace.LiveWindowCount.Should().Be(2);
        workspace.Compare(a, b, lease, epoch)
            .Should().Be(EphemeralCandidateRosterGroupsState
                .Comparison.DisjointRosterCandidates);
        new CurrentAccountProfessionsState().ActiveSnapshot.Should().BeNull();
        new CurrentCharacterState().Characters.Should().BeEmpty();
        harness.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task OptInRosterReplayIsInvalidatedByManualServerChangeAndCaptureRestart()
    {
        await using RoutingHarness harness = await RoutingHarness.CreateAsync(
            (server, workspace) => new EphemeralCandidateRosterWireObserver(server, workspace));
        var workspace = harness.RosterWorkspace!;
        DofusClientWindowKey a = new(101, 1201);
        long firstEpoch = harness.Service.CaptureEpochForReplay;

        await harness.Service.ReplayMessageAsync("S→C", "ksc",
            CandidateRoster((101, "Sable-Ciel")),
            ObservedAt, connectionId: 11, candidateWindow: a);
        await harness.Service.ReplayMessageAsync("S→C", "ksb",
            CandidateSelected(101, "Sable-Ciel"),
            ObservedAt.AddSeconds(1), connectionId: 11, candidateWindow: a);
        workspace.LiveWindowCount.Should().Be(1);

        harness.ServerState.SelectServer("OTHER_SERVER");
        workspace.LiveWindowCount.Should().Be(0);
        harness.Service.Stop();
        harness.Service.ResumeReplayForTests();
        workspace.LiveWindowCount.Should().Be(0);

        await harness.Service.ReplayMessageAsync("S→C", "ksc",
            CandidateRoster((101, "Sable-Ciel")),
            ObservedAt.AddSeconds(2),
            recordedCaptureEpoch: firstEpoch,
            connectionId: 12, candidateWindow: a);
        workspace.LiveWindowCount.Should().Be(0);
    }

    [Fact]
    public async Task WithoutExplicitOptInNoRosterGroupsAreCreatedByWireReplay()
    {
        await using RoutingHarness harness = await RoutingHarness.CreateAsync();
        DofusClientWindowKey a = new(101, 1201);
        await harness.Service.ReplayMessageAsync("S→C", "ksc",
            CandidateRoster((101, "Sable-Ciel")), ObservedAt,
            connectionId: 11, candidateWindow: a);
        await harness.Service.ReplayMessageAsync("S→C", "ksb",
            CandidateSelected(101, "Sable-Ciel"), ObservedAt.AddSeconds(1),
            connectionId: 11, candidateWindow: a);
        harness.RosterWorkspace.Should().BeNull();
        new CurrentCharacterState().Characters.Should().BeEmpty();
        new CurrentAccountProfessionsState().ActiveSnapshot.Should().BeNull();
    }

    [Fact]
    public async Task VerifiedWindowsGetSeparateShadowEquipmentWithoutChangingLegacyGlobalState()
    {
        await using RoutingHarness harness = await RoutingHarness.CreateAsync();
        DofusClientWindowKey a = new(101, 1201);
        DofusClientWindowKey b = new(102, 1202);
        long epoch = harness.Service.CaptureEpochForReplay;
        NetworkCaptureLease lease = harness.ServerState.GetCaptureLease()!.Value;

        await harness.ReplayAsync(
            "jzs", Message(Unsigned(2, 9), Unsigned(3, 42)),
            ObservedAt, connectionId: 11, candidateWindow: a);
        await harness.ReplayAsync(
            "jzs", Message(Unsigned(2, 9), Unsigned(3, 43)),
            ObservedAt.AddSeconds(1), connectionId: 22,
            candidateWindow: b);

        harness.WindowEquipment.Get(a, lease, epoch)!.DofusDbId.Should().Be(42);
        harness.WindowEquipment.Get(b, lease, epoch)!.DofusDbId.Should().Be(43);
        // P3m.17 only shadows attribution. The wheel/global behavior is
        // unchanged until there is an explicitly safe window-targeted reader.
        harness.LastEquipment.GetForServer(RoutingHarness.Server)!
            .DofusDbId.Should().Be(43);
    }

    [Fact]
    public async Task UnverifiedWindowOrRuneCannotProduceWindowEquipment()
    {
        await using RoutingHarness harness = await RoutingHarness.CreateAsync();
        DofusClientWindowKey a = new(101, 1201);
        long epoch = harness.Service.CaptureEpochForReplay;
        NetworkCaptureLease lease = harness.ServerState.GetCaptureLease()!.Value;

        await harness.ReplayAsync(
            "jzn", Market(42),
            ObservedAt, connectionId: 11);

        harness.WindowEquipment.Get(a, lease, epoch).Should().BeNull();
        harness.LastEquipment.GetForServer(RoutingHarness.Server)!
            .DofusDbId.Should().Be(42);

        await harness.ReplayAsync(
            "jzs", JzsMarket(100, 78,
                JzsOffer(100, 301, 78, 139, 1399, 14284, 151014)),
            ObservedAt.AddSeconds(1), connectionId: 22,
            candidateWindow: a);

        harness.WindowEquipment.Get(a, lease, epoch).Should().BeNull();
    }

    [Fact]
    public async Task WindowShadowIgnoresConflictingConnectionContext()
    {
        await using RoutingHarness harness = await RoutingHarness.CreateAsync();
        DofusClientWindowKey a = new(101, 1201);
        DofusClientWindowKey b = new(102, 1202);
        long epoch = harness.Service.CaptureEpochForReplay;
        NetworkCaptureLease lease = harness.ServerState.GetCaptureLease()!.Value;

        await harness.ReplayAsync(
            "jzn", Market(42), ObservedAt,
            connectionId: 11, candidateWindow: a);
        await harness.ReplayAsync(
            "jzn", Market(43), ObservedAt.AddSeconds(1),
            connectionId: 11, candidateWindow: b);

        harness.WindowEquipment.Get(a, lease, epoch)!.DofusDbId.Should().Be(42);
        harness.WindowEquipment.Get(b, lease, epoch).Should().BeNull();
        harness.LastEquipment.GetForServer(RoutingHarness.Server)!
            .DofusDbId.Should().Be(42);
    }

    [Fact]
    public async Task WindowShadowDoesNotSurviveServerChangeOrCaptureRestart()
    {
        await using RoutingHarness harness = await RoutingHarness.CreateAsync();
        DofusClientWindowKey a = new(101, 1201);
        long firstEpoch = harness.Service.CaptureEpochForReplay;
        NetworkCaptureLease firstLease = harness.ServerState.GetCaptureLease()!.Value;

        await harness.ReplayAsync(
            "jzn", Market(42), ObservedAt,
            connectionId: 11, candidateWindow: a);
        harness.WindowEquipment.Get(a, firstLease, firstEpoch).Should().NotBeNull();

        harness.ServerState.SelectServer("OTHER_SERVER");
        harness.WindowEquipment.Get(a, firstLease, firstEpoch).Should().BeNull();

        NetworkCaptureLease secondLease = harness.ServerState.GetCaptureLease()!.Value;
        await harness.ReplayAsync(
            "jzn", Market(43), ObservedAt.AddSeconds(1),
            connectionId: 12, candidateWindow: a);
        harness.WindowEquipment.Get(a, secondLease, firstEpoch)!.DofusDbId
            .Should().Be(43);

        harness.Service.Stop();
        harness.WindowEquipment.Get(a, secondLease, firstEpoch).Should().BeNull();
        harness.Service.ResumeReplayForTests();
        harness.WindowEquipment.Get(a, secondLease,
            harness.Service.CaptureEpochForReplay).Should().BeNull();
    }

    [Fact]
    public async Task JzsSelectionFocusesKnownEquipmentWithoutWritingPrices()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        byte[] response = Message(
            Bytes(1, Message(
                Unsigned(1, 42),
                Unsigned(3, 28116),
                Unsigned(4, 9))),
            Unsigned(2, 9),
            Unsigned(3, 42));

        await harness.ReplayAsync("jzs", response, ObservedAt);

        harness.LastEquipment
            .GetForServer(RoutingHarness.Server)
            .Should().Be(
                new LastNetworkEquipmentSnapshot(
                    42,
                    RoutingHarness.Server,
                    ObservedAt));

        (await harness.ReadPricesAsync(
                MarketObjectType.Equipment, 42))
            .Should().BeEmpty();

        harness.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task JzsVerifiedX1EquipmentPricesPersistMinimumAndNotify()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "jzs",
            JzsMarket(42, 11,
                JzsOffer(42, 128998, 11, 5198, 0, 0, 0),
                JzsOffer(42, 129656, 11, 4900, 0, 0, 0),
                JzsOffer(42, 129880, 11, 4899, 0, 0, 0),
                JzsOffer(42, 129374, 11, 5000, 0, 0, 0)),
            ObservedAt);

        IReadOnlyList<MarketPriceObservation> prices =
            await harness.ReadPricesAsync(MarketObjectType.Equipment, 42);

        prices.Should().ContainSingle()
            .Which.Price.Should().Be(4899L);

        prices.Single().Quantity.Should().Be(1);

        harness.Notifications.Should().ContainSingle()
            .Which.Quantity.Should().Be(1);

        harness.LastEquipment.GetForServer(RoutingHarness.Server)
            .Should().Be(new LastNetworkEquipmentSnapshot(
                42, RoutingHarness.Server, ObservedAt));
    }

    [Fact]
    public async Task JzsFollowingEmptyResponseDoesNotOverwritePriceOrNotify()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "jzs",
            JzsMarket(42, 11,
                JzsOffer(42, 129880, 11, 4899, 0, 0, 0)),
            ObservedAt);

        await harness.ReplayAsync(
            "jzs",
            JzsMarket(42, 11),
            ObservedAt.AddSeconds(1));

        (await harness.ReadPricesAsync(MarketObjectType.Equipment, 42))
            .Should().ContainSingle()
            .Which.Price.Should().Be(4899L);

        harness.Notifications.Should().ContainSingle();
    }

    [Theory]
    [InlineData(MarketObjectType.Rune, 100L, 78UL,
                139L, 1399L, 14284L, 151014L)]
    [InlineData(MarketObjectType.Resource, 200L, 104UL,
                483L, 897L, 9179L, 85000L)]
    public async Task JzsStackablePricesPersistAllFourLotsAndNotifyInLotOrder(
        MarketObjectType objectType, long itemId, ulong category,
        long one, long ten, long hundred, long thousand)
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "jzs",
            JzsMarket((ulong)itemId, category,
                JzsOffer((ulong)itemId, 13084, category,
                         (ulong)one, (ulong)ten, (ulong)hundred,
                         (ulong)thousand)),
            ObservedAt);

        IReadOnlyList<MarketPriceObservation> prices =
            await harness.ReadPricesAsync(objectType, itemId);

        prices.OrderBy(price => price.Quantity)
            .Select(price => (price.Quantity, price.Price))
            .Should().Equal(
                (1, one), (10, ten), (100, hundred), (1000, thousand));

        harness.Notifications
            .Select(change => (change.ObjectType, change.DofusDbId,
                               change.Quantity))
            .Should().Equal(
                (objectType, itemId, 1),
                (objectType, itemId, 10),
                (objectType, itemId, 100),
                (objectType, itemId, 1000));

        harness.LastEquipment
            .GetForServer(RoutingHarness.Server)
            .Should().BeNull();
    }

    [Fact]
    public async Task JzsStackableMinimaIgnoreZerosInEachSlot()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "jzs",
            JzsMarket(200, 104,
                JzsOffer(200, 100, 104, 600, 1000, 0, 85000),
                JzsOffer(200, 101, 104, 483, 0, 9179, 90000),
                JzsOffer(200, 102, 104, 0, 897, 10000, 0)),
            ObservedAt);

        (await harness.ReadPricesAsync(MarketObjectType.Resource, 200))
            .OrderBy(price => price.Quantity)
            .Select(price => (price.Quantity, price.Price))
            .Should().Equal(
                (1, 483L),
                (10, 897L),
                (100, 9179L),
                (1000, 85000L));

        harness.Notifications.Select(change => change.Quantity)
            .Should().Equal(1, 10, 100, 1000);
    }

    [Fact]
    public async Task JzsMissingX1AndX1000NeverPersistZeroPricedLots()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "jzs",
            JzsMarket(100, 78,
                JzsOffer(100, 100, 78, 0, 1399, 14284, 0)),
            ObservedAt);

        (await harness.ReadPricesAsync(MarketObjectType.Rune, 100))
            .OrderBy(price => price.Quantity)
            .Select(price => (price.Quantity, price.Price))
            .Should().Equal(
                (10, 1399L),
                (100, 14284L));

        harness.Notifications.Select(change => change.Quantity)
            .Should().Equal(10, 100);
    }

    [Fact]
    public async Task JzsEmptyStackableFollowUpNeverOverwritesPrices()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "jzs",
            JzsMarket(100, 78,
                JzsOffer(100, 100, 78, 139, 1399, 14284, 151014)),
            ObservedAt);

        await harness.ReplayAsync(
            "jzs",
            JzsMarket(100, 78),
            ObservedAt.AddSeconds(1));

        (await harness.ReadPricesAsync(MarketObjectType.Rune, 100))
            .Should().HaveCount(4);

        harness.Notifications.Should().HaveCount(4);
    }

    [Theory]
    [InlineData(MarketObjectType.Rune, 100L)]
    [InlineData(MarketObjectType.Resource, 200L)]
    public async Task JzsStackableCaptureFlagsBlockWrites(
        MarketObjectType objectType, long itemId)
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        if (objectType == MarketObjectType.Rune)
            harness.Settings.RuneCaptureEnabled = false;
        else
            harness.Settings.ResourceCaptureEnabled = false;

        await harness.ReplayAsync(
            "jzs",
            JzsMarket((ulong)itemId, 78,
                JzsOffer((ulong)itemId, 100, 78,
                         139, 1399, 14284, 151014)),
            ObservedAt);

        (await harness.ReadPricesAsync(objectType, itemId))
            .Should().BeEmpty();
        harness.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task JzsPriceDataCannotChangeLastEquipmentFocus()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "jzs",
            JzsMarket(42, 11,
                JzsOffer(42, 100, 11, 4899, 0, 0, 0)),
            ObservedAt);

        await harness.ReplayAsync(
            "jzs",
            JzsMarket(100, 78,
                JzsOffer(100, 101, 78, 139, 1399, 14284, 151014)),
            ObservedAt.AddSeconds(1));

        await harness.ReplayAsync(
            "jzs",
            JzsMarket(200, 104,
                JzsOffer(200, 102, 104, 483, 897, 9179, 85000)),
            ObservedAt.AddSeconds(2));

        harness.LastEquipment.GetForServer(RoutingHarness.Server)
            .Should().Be(new LastNetworkEquipmentSnapshot(
                42, RoutingHarness.Server, ObservedAt));
        (await harness.ReadPricesAsync(MarketObjectType.Equipment, 42))
            .Should().ContainSingle()
            .Which.Price.Should().Be(4899L);
    }

    [Fact]
    public async Task JzsUnverifiedStackableFormatRejectsWholeObservation()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "jzs",
            JzsMarket(200, 104,
                JzsOffer(200, 100, 104, 483, 897, 9179, 85000),
                JzsOffer(200, 101, 104, 500, 1000, 10000, 90000, 5)),
            ObservedAt);

        (await harness.ReadPricesAsync(MarketObjectType.Resource, 200))
            .Should().BeEmpty();
        harness.Notifications.Should().BeEmpty();
    }

    [Theory]
    [InlineData(MarketObjectType.Rune, 100L)]
    [InlineData(MarketObjectType.Resource, 200L)]
    public async Task JzsNoSelectedServerCannotSaveStackablePrices(
        MarketObjectType objectType, long itemId)
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        harness.ServerState.Clear();

        await harness.ReplayAsync(
            "jzs",
            JzsMarket((ulong)itemId, 78,
                JzsOffer((ulong)itemId, 100, 78,
                         139, 1399, 14284, 151014)),
            ObservedAt);

        (await harness.ReadPricesAsync(objectType, itemId))
            .Should().BeEmpty();
        harness.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task JzsOldServerLeaseCannotWriteRunePricesOnNewServer()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        NetworkCaptureLease oldLease =
            harness.ServerState.GetCaptureLease()!.Value;

        harness.ServerState.SelectServer("OTHER_SERVER");

        await harness.Service.ReplayMessageAsync(
            "TEST",
            "jzs",
            JzsMarket(100, 78,
                JzsOffer(100, 100, 78, 139, 1399, 14284, 151014)),
            ObservedAt,
            recordedLease: oldLease);

        (await harness.ReadPricesAsync(
                MarketObjectType.Rune, 100, RoutingHarness.Server))
            .Should().BeEmpty();

        (await harness.ReadPricesAsync(
                MarketObjectType.Rune, 100, "OTHER_SERVER"))
            .Should().BeEmpty();

        harness.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task DisabledEquipmentCaptureCannotPersistJzsPrices()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        harness.Settings.EquipmentCaptureEnabled = false;

        await harness.ReplayAsync(
            "jzs",
            JzsMarket(42, 11,
                JzsOffer(42, 100, 11, 4899, 0, 0, 0)),
            ObservedAt);

        (await harness.ReadPricesAsync(MarketObjectType.Equipment, 42))
            .Should().BeEmpty();

        harness.Notifications.Should().BeEmpty();
        harness.LastEquipment.GetForServer(RoutingHarness.Server)
            .Should().NotBeNull();
    }

    [Fact]
    public async Task JzsMalformedOrUnverifiedAdditionalLotCannotWriteSqlite()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "jzs",
            JzsMarket(42, 11,
                JzsOffer(42, 100, 11, 4899, 0, 0, 0),
                JzsOffer(42, 101, 11, 4900, 10, 0, 0)),
            ObservedAt);

        (await harness.ReadPricesAsync(MarketObjectType.Equipment, 42))
            .Should().BeEmpty();
        harness.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task JzsCannotPersistWithoutSelectedServer()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        harness.ServerState.Clear();

        await harness.ReplayAsync(
            "jzs",
            JzsMarket(42, 11,
                JzsOffer(42, 100, 11, 4899, 0, 0, 0)),
            ObservedAt);

        (await harness.ReadPricesAsync(MarketObjectType.Equipment, 42))
            .Should().BeEmpty();
        harness.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task OldJzsLeaseCannotPersistAfterSwitchingServer()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        NetworkCaptureLease oldLease =
            harness.ServerState.GetCaptureLease()!.Value;

        harness.ServerState.SelectServer("OTHER_SERVER");

        await harness.Service.ReplayMessageAsync(
            "TEST",
            "jzs",
            JzsMarket(42, 11,
                JzsOffer(42, 100, 11, 4899, 0, 0, 0)),
            ObservedAt,
            recordedLease: oldLease);

        (await harness.ReadPricesAsync(
                MarketObjectType.Equipment, 42, RoutingHarness.Server))
            .Should().BeEmpty();

        (await harness.ReadPricesAsync(
                MarketObjectType.Equipment, 42, "OTHER_SERVER"))
            .Should().BeEmpty();

        harness.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task JzsEmptySelectionStillIdentifiesEquipment()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "jzs",
            Message(Unsigned(2, 3), Unsigned(3, 43)),
            ObservedAt);

        harness.LastEquipment
            .GetForServer(RoutingHarness.Server)
            .Should().Be(
                new LastNetworkEquipmentSnapshot(
                    43,
                    RoutingHarness.Server,
                    ObservedAt));
    }

    [Fact]
    public async Task JzsResourceSelectionDoesNotReplaceLastEquipment()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "jzs",
            Message(Unsigned(2, 9), Unsigned(3, 42)),
            ObservedAt);

        await harness.ReplayAsync(
            "jzs",
            Message(Unsigned(2, 9), Unsigned(3, 200)),
            ObservedAt.AddSeconds(1));

        harness.LastEquipment
            .GetForServer(RoutingHarness.Server)
            .Should().Be(
                new LastNetworkEquipmentSnapshot(
                    42,
                    RoutingHarness.Server,
                    ObservedAt));
    }

    [Fact]
    public async Task IncompleteJzsCannotChangeExistingEquipmentFocus()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "jzs",
            Message(Unsigned(2, 9), Unsigned(3, 42)),
            ObservedAt);

        await harness.ReplayAsync(
            "jzs",
            Message(Unsigned(3, 43)),
            ObservedAt.AddSeconds(1));

        harness.LastEquipment
            .GetForServer(RoutingHarness.Server)
            .Should().Be(
                new LastNetworkEquipmentSnapshot(
                    42,
                    RoutingHarness.Server,
                    ObservedAt));
    }

    [Fact]
    public async Task JznPersistsMinimumPositivePerLotAndNotifiesInLotOrder()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "jzn",
            Market(
                42,
                MarketOffer(501, 42, 12, 0, 850, 8000),
                MarketOffer(502, 42, 15, 110, 800, 0),
                MarketOffer(503, 42, 9)),
            ObservedAt);

        IReadOnlyList<MarketPriceObservation> prices =
            await harness.ReadPricesAsync(
                MarketObjectType.Equipment,
                42);

        prices
            .OrderBy(price => price.Quantity)
            .Select(price => (price.Quantity, price.Price))
            .Should()
            .Equal(
                (1, 9L),
                (10, 110L),
                (100, 800L),
                (1000, 8000L));

        harness.Notifications
            .Select(change => change.Quantity)
            .Should()
            .Equal(1, 10, 100, 1000);

        harness.LastEquipment
            .GetForServer(RoutingHarness.Server)
            .Should()
            .Be(
                new LastNetworkEquipmentSnapshot(
                    42,
                    RoutingHarness.Server,
                    ObservedAt));
    }

    [Fact]
    public async Task EmptyJznStillIdentifiesEquipmentWithoutPersistingAPrice()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "jzn",
            Market(43),
            ObservedAt);

        (await harness.ReadPricesAsync(
                MarketObjectType.Equipment,
                43))
            .Should()
            .BeEmpty();

        harness.Notifications.Should().BeEmpty();

        harness.LastEquipment
            .GetForServer(RoutingHarness.Server)
            .Should()
            .Be(
                new LastNetworkEquipmentSnapshot(
                    43,
                    RoutingHarness.Server,
                    ObservedAt));
    }

    [Fact]
    public async Task IdenticalJznStillNotifiesWhenMarketServiceDeduplicatesPersistence()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        byte[] body =
            Market(
                200,
                MarketOffer(
                    501,
                    200,
                    25));

        await harness.ReplayAsync(
            "jzn",
            body,
            ObservedAt);

        await harness.ReplayAsync(
            "jzn",
            body,
            ObservedAt.AddSeconds(1));

        (await harness.ReadPricesAsync(
                MarketObjectType.Resource,
                200))
            .Should()
            .ContainSingle();

        harness.Notifications
            .Should()
            .HaveCount(2);

        harness.Notifications
            .Should()
            .OnlyContain(change =>
                change.ObjectType ==
                    MarketObjectType.Resource &&
                change.DofusDbId == 200 &&
                change.Quantity == 1);
    }

    [Fact]
    public async Task JznKeepsDecoderReversalBeforeSkippingUnsignedOverflow()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "jzn",
            Market(
                200,
                MarketOffer(
                    501,
                    200,
                    ulong.MaxValue,
                    100)),
            ObservedAt);

        IReadOnlyList<MarketPriceObservation> prices =
            await harness.ReadPricesAsync(
                MarketObjectType.Resource,
                200);

        prices
            .Select(price =>
                (price.Quantity, price.Price))
            .Should()
            .Equal((1, 100L));

        harness.Notifications
            .Select(change => change.Quantity)
            .Should()
            .Equal(1);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task KefPurchaseWindowIsInclusiveAtExactlyFiveSeconds(
        int extraTicks,
        bool expectedPersistence)
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "kei",
            PurchaseRequest(
                150,
                10,
                501),
            ObservedAt);

        await harness.ReplayAsync(
            "kef",
            PurchaseOffer(
                200,
                501,
                12,
                100,
                900,
                8000),
            ObservedAt
                .AddSeconds(5)
                .AddTicks(extraTicks));

        IReadOnlyList<MarketPriceObservation> prices =
            await harness.ReadPricesAsync(
                MarketObjectType.Resource,
                200);

        prices
            .Should()
            .HaveCount(
                expectedPersistence
                    ? 4
                    : 0);
    }

    [Fact]
    public async Task NewPurchaseRequestReplacesPreviousCorrelationAndReceiptClearsIt()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "kei",
            PurchaseRequest(100, 1, 501),
            ObservedAt);

        await harness.ReplayAsync(
            "kei",
            PurchaseRequest(200, 1, 502),
            ObservedAt.AddSeconds(1));

        await harness.ReplayAsync(
            "kef",
            PurchaseOffer(200, 501, 10),
            ObservedAt.AddSeconds(2));

        await harness.ReplayAsync(
            "kef",
            PurchaseOffer(201, 502, 20),
            ObservedAt.AddSeconds(2));

        (await harness.ReadPricesAsync(
                MarketObjectType.Resource,
                200))
            .Should()
            .BeEmpty();

        (await harness.ReadPricesAsync(
                MarketObjectType.Resource,
                201))
            .Should()
            .ContainSingle();

        await harness.ReplayAsync(
            "kei",
            PurchaseRequest(300, 1, 503),
            ObservedAt.AddSeconds(3));

        await harness.ReplayAsync(
            "kbd",
            PurchaseReceipt(
                1,
                503),
            ObservedAt.AddSeconds(4));

        await harness.ReplayAsync(
            "kef",
            PurchaseOffer(202, 503, 30),
            ObservedAt.AddSeconds(5));

        (await harness.ReadPricesAsync(
                MarketObjectType.Resource,
                202))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public async Task ConflictingVerifiedWindowsQuarantinePurchaseForEntireConnection()
    {
        await using RoutingHarness harness = await RoutingHarness.CreateAsync();
        DofusClientWindowKey clientA = new(101, 1201);
        DofusClientWindowKey clientB = new(102, 1202);

        await harness.ReplayAsync(
            "kei", PurchaseRequest(100, 1, 601),
            ObservedAt, connectionId: 11, candidateWindow: clientA);

        // A contradictory verified window for the SAME connection ID
        // invalidates its pending request. Reverting to A is insufficient.
        await harness.ReplayAsync(
            "kef", PurchaseOffer(100, 601, 15, 140),
            ObservedAt.AddSeconds(1), connectionId: 11,
            candidateWindow: clientB);
        await harness.ReplayAsync(
            "kef", PurchaseOffer(100, 601, 15, 140),
            ObservedAt.AddSeconds(2), connectionId: 11,
            candidateWindow: clientA);

        (await harness.ReadPricesAsync(MarketObjectType.Rune, 100))
            .Should().BeEmpty();

        // A fresh logical TCP connection has an independent, clean scope.
        await harness.ReplayAsync(
            "kei", PurchaseRequest(100, 1, 601),
            ObservedAt.AddSeconds(3), connectionId: 12,
            candidateWindow: clientA);
        await harness.ReplayAsync(
            "kef", PurchaseOffer(100, 601, 15, 140),
            ObservedAt.AddSeconds(4), connectionId: 12,
            candidateWindow: clientA);

        (await harness.ReadPricesAsync(MarketObjectType.Rune, 100))
            .Should().HaveCount(2);
    }

    [Fact]
    public async Task ConflictingVerifiedWindowsCannotBorrowOrRestoreItemUid()
    {
        await using RoutingHarness harness = await RoutingHarness.CreateAsync();
        DofusClientWindowKey clientA = new(101, 1201);
        DofusClientWindowKey clientB = new(102, 1202);

        await harness.ReplayAsync(
            "kch", CurrentItemDetail(9022491, 42),
            ObservedAt, connectionId: 11, candidateWindow: clientA);
        byte[] crush = CurrentCrush(
            CurrentCrushRow(9022491, 1.615723f,
                CurrentCrushRune(100, 6)));

        await harness.ReplayAsync(
            "kag", crush, ObservedAt.AddSeconds(1),
            connectionId: 11, candidateWindow: clientB);
        await harness.ReplayAsync(
            "kag", crush, ObservedAt.AddSeconds(2),
            connectionId: 11, candidateWindow: clientA);

        harness.Crushes.Should().BeEmpty();
        (await harness.ReadCoefficientsAsync(42)).Should().BeEmpty();

        await harness.ReplayAsync(
            "kch", CurrentItemDetail(9022491, 42),
            ObservedAt.AddSeconds(3), connectionId: 12,
            candidateWindow: clientA);
        await harness.ReplayAsync(
            "kag", crush, ObservedAt.AddSeconds(4),
            connectionId: 12, candidateWindow: clientA);
        harness.Crushes.Should().ContainSingle();
    }

    [Fact]
    public async Task QuarantinedTokenCannotReviveWhenCorrelationsAreEvicted()
    {
        await using RoutingHarness harness = await RoutingHarness.CreateAsync();
        DofusClientWindowKey clientA = new(101, 1201);
        DofusClientWindowKey clientB = new(102, 1202);

        await harness.ReplayAsync(
            "kei", PurchaseRequest(100, 1, 601),
            ObservedAt, connectionId: 11, candidateWindow: clientA);
        await harness.ReplayAsync(
            "kei", PurchaseRequest(100, 1, 601),
            ObservedAt.AddSeconds(1), connectionId: 11,
            candidateWindow: clientB);

        // Exhaust the bounded per-flow cache, but not the independent
        // quarantine. An evicted token must not be accepted again.
        for (int index = 0; index < 70; index++)
        {
            await harness.ReplayAsync(
                "kei", PurchaseRequest(100, 1, (ulong)(700 + index)),
                ObservedAt.AddSeconds(index + 2),
                connectionId: 100 + index, candidateWindow: clientA);
        }

        await harness.ReplayAsync(
            "kei", PurchaseRequest(100, 1, 601),
            ObservedAt.AddMinutes(3), connectionId: 11,
            candidateWindow: clientA);
        await harness.ReplayAsync(
            "kef", PurchaseOffer(100, 601, 15, 140),
            ObservedAt.AddMinutes(3).AddSeconds(1),
            connectionId: 11, candidateWindow: clientA);

        (await harness.ReadPricesAsync(MarketObjectType.Rune, 100))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task FirstVerifiedWindowSurvivesBusinessCacheEviction()
    {
        await using RoutingHarness harness = await RoutingHarness.CreateAsync();
        DofusClientWindowKey clientA = new(101, 1201);
        DofusClientWindowKey clientB = new(102, 1202);

        await harness.ReplayAsync(
            "kei", PurchaseRequest(100, 1, 601),
            ObservedAt, connectionId: 11, candidateWindow: clientA);

        for (int index = 0; index < 70; index++)
        {
            await harness.ReplayAsync(
                "kei", PurchaseRequest(100, 1, (ulong)(700 + index)),
                ObservedAt.AddSeconds(index + 1),
                connectionId: 100 + index, candidateWindow: clientA);
        }

        // Although the flow-11 purchase cache was evicted, its verified
        // window assignment must still reject a contradictory client B.
        await harness.ReplayAsync(
            "kei", PurchaseRequest(100, 1, 601),
            ObservedAt.AddMinutes(3), connectionId: 11,
            candidateWindow: clientB);
        await harness.ReplayAsync(
            "kei", PurchaseRequest(100, 1, 601),
            ObservedAt.AddMinutes(3).AddSeconds(1),
            connectionId: 11, candidateWindow: clientA);
        await harness.ReplayAsync(
            "kef", PurchaseOffer(100, 601, 15, 140),
            ObservedAt.AddMinutes(3).AddSeconds(2),
            connectionId: 11, candidateWindow: clientA);

        (await harness.ReadPricesAsync(MarketObjectType.Rune, 100))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task CaptureRestartClearsOldWindowQuarantine()
    {
        await using RoutingHarness harness = await RoutingHarness.CreateAsync();
        DofusClientWindowKey clientA = new(101, 1201);
        DofusClientWindowKey clientB = new(102, 1202);
        byte[] crush = CurrentCrush(
            CurrentCrushRow(9022491, 1.615723f,
                CurrentCrushRune(100, 6)));

        await harness.ReplayAsync(
            "kch", CurrentItemDetail(9022491, 42),
            ObservedAt, connectionId: 11, candidateWindow: clientA);
        await harness.ReplayAsync(
            "kag", crush, ObservedAt.AddSeconds(1),
            connectionId: 11, candidateWindow: clientB);

        harness.Service.Stop();
        harness.Service.ResumeReplayForTests();

        await harness.ReplayAsync(
            "kch", CurrentItemDetail(9022491, 42),
            ObservedAt.AddSeconds(2), connectionId: 11,
            candidateWindow: clientA);
        await harness.ReplayAsync(
            "kag", crush, ObservedAt.AddSeconds(3),
            connectionId: 11, candidateWindow: clientA);

        harness.Crushes.Should().ContainSingle();
    }

    [Fact]
    public async Task ReconnectToSameWindowRequiresNewUidAndPurchaseCorrelations()
    {
        await using RoutingHarness harness = await RoutingHarness.CreateAsync();
        DofusClientWindowKey clientA = new(101, 1201);
        byte[] crush = CurrentCrush(
            CurrentCrushRow(9022491, 1.615723f,
                CurrentCrushRune(100, 6)));

        await harness.ReplayAsync(
            "kch", CurrentItemDetail(9022491, 42),
            ObservedAt, connectionId: 11, candidateWindow: clientA);
        await harness.ReplayAsync(
            "kei", PurchaseRequest(100, 1, 601),
            ObservedAt.AddSeconds(1), connectionId: 11,
            candidateWindow: clientA);

        // Same physical window and same item/offer identifiers, but a new
        // logical TCP connection following character re-selection.
        await harness.ReplayAsync(
            "kag", crush, ObservedAt.AddSeconds(2),
            connectionId: 12, candidateWindow: clientA);
        await harness.ReplayAsync(
            "kef", PurchaseOffer(100, 601, 15, 140),
            ObservedAt.AddSeconds(3), connectionId: 12,
            candidateWindow: clientA);

        harness.Crushes.Should().BeEmpty();
        (await harness.ReadCoefficientsAsync(42)).Should().BeEmpty();
        (await harness.ReadPricesAsync(MarketObjectType.Rune, 100))
            .Should().BeEmpty();

        await harness.ReplayAsync(
            "kch", CurrentItemDetail(9022491, 42),
            ObservedAt.AddSeconds(4), connectionId: 12,
            candidateWindow: clientA);
        await harness.ReplayAsync(
            "kei", PurchaseRequest(100, 1, 601),
            ObservedAt.AddSeconds(5), connectionId: 12,
            candidateWindow: clientA);
        await harness.ReplayAsync(
            "kag", crush, ObservedAt.AddSeconds(6),
            connectionId: 12, candidateWindow: clientA);
        await harness.ReplayAsync(
            "kef", PurchaseOffer(100, 601, 15, 140),
            ObservedAt.AddSeconds(7), connectionId: 12,
            candidateWindow: clientA);

        harness.Crushes.Should().ContainSingle();
        (await harness.ReadCoefficientsAsync(42)).Should().ContainSingle();
        (await harness.ReadPricesAsync(MarketObjectType.Rune, 100))
            .Should().HaveCount(2);
    }

    [Fact]
    public async Task TwoClientsCannotSharePendingPurchaseEvenForSameOfferId()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        // A issues a request. B receives the matching offer but did
        // not request anything. The offer must NOT be persisted.
        await harness.ReplayAsync(
            "kei", PurchaseRequest(100, 1, 601),
            ObservedAt, connectionId: 11);
        await harness.ReplayAsync(
            "kef", PurchaseOffer(100, 601, 15, 140),
            ObservedAt.AddSeconds(1), connectionId: 22);

        (await harness.ReadPricesAsync(MarketObjectType.Rune, 100))
            .Should().BeEmpty();

        // A still owns its request; response on A succeeds.
        await harness.ReplayAsync(
            "kef", PurchaseOffer(100, 601, 15, 140),
            ObservedAt.AddSeconds(2), connectionId: 11);

        (await harness.ReadPricesAsync(MarketObjectType.Rune, 100))
            .OrderBy(price => price.Quantity)
            .Select(price => (price.Quantity, price.Price))
            .Should().Equal((1, 15L), (10, 140L));
    }

    [Fact]
    public async Task OtherClientReceiptCannotClearPendingPurchase()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "kei", PurchaseRequest(100, 1, 601),
            ObservedAt, connectionId: 11);
        await harness.ReplayAsync(
            "kbd", PurchaseReceipt(1, 601),
            ObservedAt.AddSeconds(1), connectionId: 22);
        await harness.ReplayAsync(
            "kef", PurchaseOffer(100, 601, 15, 140),
            ObservedAt.AddSeconds(2), connectionId: 11);

        (await harness.ReadPricesAsync(MarketObjectType.Rune, 100))
            .Should().HaveCount(2);
    }

    [Fact]
    public async Task SameItemUidOnAnotherClientCannotResolveCrush()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        // Even an identical item UID is not sufficient across clients.
        await harness.ReplayAsync(
            "kch", CurrentItemDetail(9022491, 42),
            ObservedAt, connectionId: 11);

        await harness.ReplayAsync(
            "kag", CurrentCrush(
                CurrentCrushRow(9022491, 1.615723f,
                    CurrentCrushRune(100, 6))),
            ObservedAt.AddSeconds(1), connectionId: 22);

        harness.Crushes.Should().BeEmpty();
        (await harness.ReadCoefficientsAsync(42)).Should().BeEmpty();

        // The owner client still retains its own UID mapping.
        await harness.ReplayAsync(
            "kag", CurrentCrush(
                CurrentCrushRow(9022491, 1.615723f,
                    CurrentCrushRune(100, 6))),
            ObservedAt.AddSeconds(2), connectionId: 11);

        harness.Crushes.Should().ContainSingle();
        harness.Crushes.Single().Lines.Single()
            .EquipmentDofusDbId.Should().Be(42);
    }

    [Fact]
    public async Task MissingFlowIdentityNeverPersistsMarketData()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "jzn",
            Market(200, MarketOffer(501, 200, 25)),
            ObservedAt, connectionId: 0);

        (await harness.ReadPricesAsync(MarketObjectType.Resource, 200))
            .Should().BeEmpty();
        harness.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task KefRefreshesRuneLikeOtherStackableObjects()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "kei",
            PurchaseRequest(100, 1, 601),
            ObservedAt);

        await harness.ReplayAsync(
            "kef",
            PurchaseOffer(100, 601, 15, 140),
            ObservedAt.AddSeconds(1));

        IReadOnlyList<MarketPriceObservation> prices =
            await harness.ReadPricesAsync(
                MarketObjectType.Rune,
                100);

        prices
            .OrderBy(price => price.Quantity)
            .Select(price => (price.Quantity, price.Price))
            .Should()
            .Equal(
                (1, 15L),
                (10, 140L));
    }

    [Fact]
    public async Task KefNeverReplacesEquipmentPriceCapturedByJzn()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "jzn",
            Market(
                42,
                MarketOffer(700, 42, 100)),
            ObservedAt);

        await harness.ReplayAsync(
            "kei",
            PurchaseRequest(50, 1, 701),
            ObservedAt.AddSeconds(1));

        DateTime refreshAt =
            ObservedAt.AddSeconds(2);

        await harness.ReplayAsync(
            "kef",
            PurchaseOffer(42, 701, 50),
            refreshAt);

        IReadOnlyList<MarketPriceObservation> prices =
            await harness.ReadPricesAsync(
                MarketObjectType.Equipment,
                42);

        prices
            .Select(price => price.Price)
            .Should()
            .Equal(100L);

        harness.LastEquipment
            .GetForServer(RoutingHarness.Server)
            .Should()
            .Be(
                new LastNetworkEquipmentSnapshot(
                    42,
                    RoutingHarness.Server,
                    refreshAt));
    }

    [Theory]
    [InlineData("kdb")]
    [InlineData("isa")]
    [InlineData("iuq")]
    [InlineData("kbu")]
    public async Task ItemMessagesRememberCataloguedEquipment(
        string key)
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        byte[] body =
            key switch
            {
                "kdb" =>
                    Bytes(
                        2,
                        Bytes(
                            5,
                            Item(8001, 42))),

                "isa" =>
                    Bytes(
                        2,
                        Bytes(
                            5,
                            Item(8001, 42))),

                "iuq" =>
                    Bytes(
                        1,
                        Bytes(
                            5,
                            Item(8001, 42))),

                "kbu" =>
                    Message(
                        Unsigned(2, 0),
                        Bytes(
                            3,
                            Bytes(
                                2,
                                Item(8001, 42)))),

                _ =>
                    throw new ArgumentOutOfRangeException(
                        nameof(key))
            };

        await harness.ReplayAsync(
            key,
            body,
            ObservedAt);

        harness.LastEquipment
            .GetForServer(RoutingHarness.Server)
            .Should()
            .Be(
                new LastNetworkEquipmentSnapshot(
                    42,
                    RoutingHarness.Server,
                    ObservedAt));
    }

    [Fact]
    public async Task KnownRuneItemNeverBecomesLastNetworkEquipment()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "kdb",
            Bytes(
                2,
                Bytes(
                    5,
                    Item(8100, 100))),
            ObservedAt);

        harness.LastEquipment
            .GetForServer(RoutingHarness.Server)
            .Should()
            .BeNull();
    }

    [Fact]
    public async Task KciSkipsUnknownUidPreservesKnownLineOrderAndLatestPositiveCoefficientWins()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "kdb",
            Bytes(
                2,
                Bytes(
                    5,
                    Item(8001, 42))),
            ObservedAt);

        DateTime crushAt =
            ObservedAt.AddSeconds(1);

        await harness.ReplayAsync(
            "kci",
            Message(
                Bytes(
                    1,
                    CrushRow(
                        9999,
                        5f,
                        Rune(100, 99))),
                Bytes(
                    1,
                    CrushRow(
                        8001,
                        0f,
                        Rune(100, 1))),
                Bytes(
                    1,
                    CrushRow(
                        8001,
                        1.25f,
                        Rune(100, 2))),
                Bytes(
                    1,
                    CrushRow(
                        8001,
                        2f,
                        Rune(100, 3)))),
            crushAt);

        harness.Crushes
            .Should()
            .ContainSingle();

        NetworkCrushCapture capture =
            harness.Crushes.Single();

        capture.ObservedAtUtc
            .Should()
            .Be(crushAt);

        capture.Lines
            .Select(line =>
                (
                    line.EquipmentDofusDbId,
                    line.CoefficientPercent,
                    RuneQuantity:
                        line.Runes
                            .Single()
                            .Quantity
                ))
            .Should()
            .Equal(
                (42L, 0d, 1),
                (42L, 125d, 2),
                (42L, 200d, 3));

        IReadOnlyList<CoefficientObservation> coefficients =
            await harness.ReadCoefficientsAsync(42);

        coefficients
            .Select(coefficient =>
                coefficient.CoefficientPercent)
            .Should()
            .BeEquivalentTo(
                new[] { 125d, 200d });

        CoefficientObservation? latest =
            await harness.ReadEffectiveCoefficientAsync(
                42);

        latest!
            .CoefficientPercent
            .Should()
            .Be(200d);

        harness.Notifications
            .Should()
            .HaveCount(2);

        harness.Notifications
            .Should()
            .OnlyContain(change =>
                change.ObjectType ==
                    MarketObjectType.Equipment &&
                change.DofusDbId == 42);

        capture.Lines
            .SelectMany(line => line.Runes)
            .Should()
            .NotContain(rune =>
                rune.Quantity == 99);
    }

    [Fact]
    public async Task DisabledCoefficientCaptureStillDeliversNetworkCrushResult()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        harness.Settings
            .CoefficientCaptureEnabled =
                false;

        await harness.ReplayAsync(
            "kdb",
            Bytes(
                2,
                Bytes(
                    5,
                    Item(8001, 42))),
            ObservedAt);

        await harness.ReplayAsync(
            "kci",
            Bytes(
                1,
                CrushRow(
                    8001,
                    1.5f,
                    Rune(100, 2))),
            ObservedAt.AddSeconds(1));

        (await harness.ReadCoefficientsAsync(42))
            .Should()
            .BeEmpty();

        harness.Crushes
            .Should()
            .ContainSingle();

        harness.Crushes
            .Single()
            .Lines
            .Should()
            .ContainSingle();

        harness.Crushes
            .Single()
            .Lines
            .Single()
            .CoefficientPercent
            .Should()
            .Be(150d);
    }

    [Fact]
    public async Task KciKeepsCheckedRuneQuantityConversion()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "kdb",
            Bytes(
                2,
                Bytes(
                    5,
                    Item(8001, 42))),
            ObservedAt);

        Func<Task> replay =
            () =>
                harness.ReplayAsync(
                    "kci",
                    Bytes(
                        1,
                        CrushRow(
                            8001,
                            1f,
                            Rune(
                                100,
                                (ulong)int.MaxValue +
                                1))),
                    ObservedAt.AddSeconds(1));

        await replay
            .Should()
            .ThrowAsync<OverflowException>();
    }

    [Fact]
    public async Task CurrentKchAndKagRouteCoefficientAndRunesThroughRealWriter()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        DateTime crushAt = ObservedAt.AddSeconds(2);
        await harness.ReplayAsync(
            "kch", CurrentItemDetail(9022491, 42, 1), ObservedAt);

        await harness.ReplayAsync(
            "kag", CurrentCrush(
                CurrentCrushRow(9022491, 1.615723f,
                    CurrentCrushRune(100, 6))), crushAt);

        harness.Crushes.Should().ContainSingle();
        NetworkCrushCapture actual = harness.Crushes.Single();
        actual.ObservedAtUtc.Should().Be(crushAt);
        actual.Lines.Should().ContainSingle();

        NetworkCrushResultLine line = actual.Lines.Single();
        line.EquipmentDofusDbId.Should().Be(42);
        line.CoefficientPercent
            .Should().BeApproximately(161.5723d, 0.0001d);
        line.Runes.Should().ContainSingle()
            .Which.Should().Be(new NetworkCrushRuneResult(100, 6));

        (await harness.ReadCoefficientsAsync(42))
            .Should().ContainSingle()
            .Which.CoefficientPercent.Should()
            .BeApproximately(161.5723d, 0.0001d);

        harness.Notifications.Should().ContainSingle()
            .Which.DofusDbId.Should().Be(42);
    }

    [Fact]
    public async Task CurrentKagWithoutRuneStillPersistsRealCoefficient()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "kch", CurrentItemDetail(11387412, 43, 1), ObservedAt);

        await harness.ReplayAsync(
            "kag", CurrentCrush(
                CurrentCrushRow(11387412, 0.5976145f)),
            ObservedAt.AddSeconds(1));

        harness.Crushes.Should().ContainSingle();
        NetworkCrushResultLine line =
            harness.Crushes.Single().Lines.Single();
        line.EquipmentDofusDbId.Should().Be(43);
        line.CoefficientPercent
            .Should().BeApproximately(59.76145d, 0.0001d);
        line.Runes.Should().BeEmpty();
        (await harness.ReadCoefficientsAsync(43))
            .Should().ContainSingle();
    }

    [Fact]
    public async Task CurrentKagMissingUidCorrelationNeverWritesCoefficient()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "kag", CurrentCrush(
                CurrentCrushRow(9022491, 1.615723f,
                    CurrentCrushRune(100, 6))),
            ObservedAt);

        harness.Crushes.Should().BeEmpty();
        (await harness.ReadCoefficientsAsync(42)).Should().BeEmpty();
        harness.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task CurrentKagWrongUidDoesNotBorrowRecentlySeenItem()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "kch", CurrentItemDetail(9022491, 42, 1), ObservedAt);

        await harness.ReplayAsync(
            "kag", CurrentCrush(
                CurrentCrushRow(9022492, 1.615723f,
                    CurrentCrushRune(100, 6))),
            ObservedAt.AddSeconds(1));

        harness.Crushes.Should().BeEmpty();
        (await harness.ReadCoefficientsAsync(42)).Should().BeEmpty();
    }

    [Fact]
    public async Task CurrentKagStillDeliversRunesWhenCoefficientCaptureIsDisabled()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        harness.Settings.CoefficientCaptureEnabled = false;
        await harness.ReplayAsync(
            "kch", CurrentItemDetail(9022491, 42), ObservedAt);
        await harness.ReplayAsync(
            "kag", CurrentCrush(
                CurrentCrushRow(9022491, 1.615723f,
                    CurrentCrushRune(100, 6))),
            ObservedAt.AddSeconds(1));

        (await harness.ReadCoefficientsAsync(42))
            .Should().BeEmpty();
        harness.Notifications.Should().BeEmpty();
        harness.Crushes.Should().ContainSingle();
        harness.Crushes.Single().Lines.Single()
            .Runes.Should().ContainSingle()
            .Which.Should().Be(new NetworkCrushRuneResult(100, 6));
    }

    [Fact]
    public async Task CurrentKagCannotUsePreviousServerUidCache()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "kch", CurrentItemDetail(9022491, 42), ObservedAt);
        harness.ServerState.SelectServer("OTHER_SERVER");

        await harness.ReplayAsync(
            "kag", CurrentCrush(
                CurrentCrushRow(9022491, 1.615723f,
                    CurrentCrushRune(100, 6))),
            ObservedAt.AddSeconds(1));

        harness.Crushes.Should().BeEmpty();
        (await harness.ReadCoefficientsAsync(42)).Should().BeEmpty();

        await harness.ReplayAsync(
            "kch", CurrentItemDetail(9022491, 42),
            ObservedAt.AddSeconds(2));

        await harness.ReplayAsync(
            "kag", CurrentCrush(
                CurrentCrushRow(9022491, 1.615723f,
                    CurrentCrushRune(100, 6))),
            ObservedAt.AddSeconds(3));

        harness.Crushes.Should().ContainSingle();
        harness.Crushes.Single().Lines.Single()
            .EquipmentDofusDbId.Should().Be(42);
    }

    [Fact]
    public async Task CurrentKagCannotUseUidCacheAfterCaptureRestart()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "kch", CurrentItemDetail(9022491, 42), ObservedAt);

        harness.Service.Stop();
        harness.Service.ResumeReplayForTests();

        await harness.ReplayAsync(
            "kag", CurrentCrush(
                CurrentCrushRow(9022491, 1.615723f)),
            ObservedAt.AddSeconds(1));

        harness.Crushes.Should().BeEmpty();
        await harness.ReplayAsync(
            "kch", CurrentItemDetail(9022491, 42),
            ObservedAt.AddSeconds(2));
        await harness.ReplayAsync(
            "kag", CurrentCrush(
                CurrentCrushRow(9022491, 1.615723f)),
            ObservedAt.AddSeconds(3));

        harness.Crushes.Should().ContainSingle();
    }

    [Fact]
    public async Task CurrentKagWithoutServerCannotPersistOrRender()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        harness.ServerState.Clear();
        await harness.ReplayAsync(
            "kch", CurrentItemDetail(9022491, 42), ObservedAt);

        await harness.ReplayAsync(
            "kag", CurrentCrush(
                CurrentCrushRow(9022491, 1.615723f,
                    CurrentCrushRune(100, 6))),
            ObservedAt.AddSeconds(1));

        harness.Crushes.Should().BeEmpty();
        (await harness.ReadCoefficientsAsync(42)).Should().BeEmpty();
    }

    [Fact]
    public async Task CurrentKagMultiItemKeepsMessageOrderAndRuneLines()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "kch", CurrentItemDetail(8001, 42), ObservedAt);
        await harness.ReplayAsync(
            "kch", CurrentItemDetail(8002, 43), ObservedAt);

        await harness.ReplayAsync(
            "kag", CurrentCrush(
                CurrentCrushRow(8001, 1.5f,
                    CurrentCrushRune(100, 2)),
                CurrentCrushRow(8002, 0.75f,
                    CurrentCrushRune(100, 3))),
            ObservedAt.AddSeconds(1));

        harness.Crushes.Should().ContainSingle();
        harness.Crushes.Single().Lines
            .Select(line => (line.EquipmentDofusDbId,
                             line.Runes.Single().Quantity))
            .Should().Equal((42L, 2), (43L, 3));
        harness.Notifications
            .Select(note => note.DofusDbId)
            .Should().Equal(42, 43);
    }

    [Fact]
    public async Task QueuedReplayUsesSingleReaderMessageOrder()
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        Task first =
            harness.ReplayAsync(
                "jzn",
                Market(
                    200,
                    MarketOffer(1, 200, 10)),
                ObservedAt);

        Task second =
            harness.ReplayAsync(
                "jzn",
                Market(
                    201,
                    MarketOffer(2, 201, 20)),
                ObservedAt.AddSeconds(1));

        Task third =
            harness.ReplayAsync(
                "jzn",
                Market(
                    202,
                    MarketOffer(3, 202, 30)),
                ObservedAt.AddSeconds(2));

        await Task.WhenAll(
            first,
            second,
            third);

        harness.Notifications
            .Select(change =>
                change.DofusDbId)
            .Should()
            .Equal(
                200,
                201,
                202);
    }

    [Theory]
    [InlineData(MarketObjectType.Equipment, 42)]
    [InlineData(MarketObjectType.Rune, 100)]
    [InlineData(MarketObjectType.Resource, 200)]
    public async Task MarketCaptureFlagsGatePersistence(
        MarketObjectType objectType,
        long dofusDbId)
    {
        await using RoutingHarness harness =
            await RoutingHarness.CreateAsync();

        switch (objectType)
        {
            case MarketObjectType.Equipment:
                harness.Settings
                    .EquipmentCaptureEnabled =
                        false;
                break;

            case MarketObjectType.Rune:
                harness.Settings
                    .RuneCaptureEnabled =
                        false;
                break;

            case MarketObjectType.Resource:
                harness.Settings
                    .ResourceCaptureEnabled =
                        false;
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(objectType));
        }

        await harness.ReplayAsync(
            "jzn",
            Market(
                (ulong)dofusDbId,
                MarketOffer(
                    1,
                    (ulong)dofusDbId,
                    10)),
            ObservedAt);

        (await harness.ReadPricesAsync(
                objectType,
                dofusDbId))
            .Should()
            .BeEmpty();

        harness.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutSelectedServerNetworkMessagesAreIgnored()
    {
        await using RoutingHarness harness = await RoutingHarness.CreateAsync();
        harness.ServerState.Clear();

        await harness.ReplayAsync(
            "jzn", Market(42, MarketOffer(501, 42, 100)), ObservedAt);

        (await harness.ReadPricesAsync(MarketObjectType.Equipment, 42))
            .Should().BeEmpty();
        harness.LastEquipment.GetForServer(RoutingHarness.Server)
            .Should().BeNull();
        harness.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task SwitchingServerDropsOldQueuedMessagesButCapturesImmediatelyOnNewServer()
    {
        await using RoutingHarness harness = await RoutingHarness.CreateAsync();
        NetworkCaptureLease oldLease = harness.ServerState.GetCaptureLease()!.Value;

        await harness.ReplayAsync(
            "jzn", Market(200, MarketOffer(1, 200, 12)), ObservedAt);

        harness.ServerState.SelectServer("OTHER_SERVER");

        await harness.Service.ReplayMessageAsync(
            "TEST", "jzn", Market(201, MarketOffer(2, 201, 15)),
            ObservedAt.AddSeconds(1),
            recordedLease: oldLease);

        await harness.ReplayAsync(
            "jzn", Market(202, MarketOffer(3, 202, 20)),
            ObservedAt.AddSeconds(2));

        (await harness.ReadPricesAsync(MarketObjectType.Resource, 200))
            .Should().ContainSingle();
        (await harness.ReadPricesAsync(MarketObjectType.Resource, 201))
            .Should().BeEmpty();
        (await harness.ReadPricesAsync(MarketObjectType.Resource, 202))
            .Should().BeEmpty();
        (await harness.ReadPricesAsync(
            MarketObjectType.Resource, 202, "OTHER_SERVER"))
            .Should().ContainSingle()
            .Which.Price.Should().Be(20L);

        harness.Notifications.Select(n => n.ServerName)
            .Should().Equal(RoutingHarness.Server, "OTHER_SERVER");
    }

    [Fact]
    public async Task SwitchingServerClearsUidCorrelationBeforeNewCrush()
    {
        await using RoutingHarness harness = await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "kdb", Bytes(2, Bytes(5, Item(8001, 42))), ObservedAt);

        harness.ServerState.SelectServer("OTHER_SERVER");

        await harness.ReplayAsync(
            "kci", Bytes(1, CrushRow(8001, 1.5f, Rune(100, 2))),
            ObservedAt.AddSeconds(1));

        harness.Crushes.Should().BeEmpty();

        await harness.ReplayAsync(
            "kdb", Bytes(2, Bytes(5, Item(8001, 42))),
            ObservedAt.AddSeconds(2));

        await harness.ReplayAsync(
            "kci", Bytes(1, CrushRow(8001, 1.5f, Rune(100, 2))),
            ObservedAt.AddSeconds(3));

        harness.Crushes.Should().ContainSingle();
        harness.Crushes.Single().Lines.Should().ContainSingle()
            .Which.EquipmentDofusDbId.Should().Be(42L);

        (await harness.ReadCoefficientsAsync(42)).Should().BeEmpty();
    }

    [Fact]
    public async Task StopRejectsReplayAndResumeProcessesFreshMessages()
    {
        await using RoutingHarness harness = await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "jzn", Market(200, MarketOffer(1, 200, 10)), ObservedAt);

        long previousEpoch = harness.Service.CaptureEpochForReplay;
        harness.Service.Stop();

        Func<Task> stoppedReplay = () => harness.ReplayAsync(
            "jzn", Market(201, MarketOffer(2, 201, 20)),
            ObservedAt.AddSeconds(1));

        await stoppedReplay.Should().ThrowAsync<InvalidOperationException>();

        harness.Service.ResumeReplayForTests();

        harness.Service.CaptureEpochForReplay.Should().NotBe(previousEpoch);

        await harness.ReplayAsync(
            "jzn", Market(201, MarketOffer(2, 201, 20)),
            ObservedAt.AddSeconds(2));

        (await harness.ReadPricesAsync(MarketObjectType.Resource, 200))
            .Should().ContainSingle();
        (await harness.ReadPricesAsync(MarketObjectType.Resource, 201))
            .Should().ContainSingle();

        harness.Notifications.Select(n => n.DofusDbId)
            .Should().Equal(200, 201);
    }

    [Fact]
    public async Task PreviousRunMessageIsRejectedEvenIfQueuedAfterResume()
    {
        await using RoutingHarness harness = await RoutingHarness.CreateAsync();

        long oldEpoch = harness.Service.CaptureEpochForReplay;
        harness.Service.Stop();
        harness.Service.ResumeReplayForTests();

        await harness.Service.ReplayMessageAsync(
            "TEST", "jzn", Market(200, MarketOffer(1, 200, 10)),
            ObservedAt, recordedCaptureEpoch: oldEpoch);

        await harness.ReplayAsync(
            "jzn", Market(201, MarketOffer(2, 201, 20)),
            ObservedAt.AddSeconds(1));

        (await harness.ReadPricesAsync(MarketObjectType.Resource, 200))
            .Should().BeEmpty();
        (await harness.ReadPricesAsync(MarketObjectType.Resource, 201))
            .Should().ContainSingle();
        harness.Notifications.Select(n => n.DofusDbId)
            .Should().Equal(201);
    }

    [Fact]
    public async Task RestartClearsItemAndPurchaseCorrelations()
    {
        await using RoutingHarness harness = await RoutingHarness.CreateAsync();

        await harness.ReplayAsync(
            "kdb", Bytes(2, Bytes(5, Item(8001, 42))),
            ObservedAt);

        await harness.ReplayAsync(
            "kei", PurchaseRequest(100, 1, 501),
            ObservedAt.AddSeconds(1));

        harness.Service.Stop();
        harness.Service.ResumeReplayForTests();

        await harness.ReplayAsync(
            "kci", Bytes(1, CrushRow(8001, 1.5f, Rune(100, 2))),
            ObservedAt.AddSeconds(2));

        await harness.ReplayAsync(
            "kef", PurchaseOffer(200, 501, 10, 100),
            ObservedAt.AddSeconds(3));

        harness.Crushes.Should().BeEmpty();
        (await harness.ReadPricesAsync(MarketObjectType.Resource, 200))
            .Should().BeEmpty();

        await harness.ReplayAsync(
            "kdb", Bytes(2, Bytes(5, Item(8001, 42))),
            ObservedAt.AddSeconds(4));

        await harness.ReplayAsync(
            "kci", Bytes(1, CrushRow(8001, 1.5f, Rune(100, 2))),
            ObservedAt.AddSeconds(5));

        harness.Crushes.Should().ContainSingle();
    }

    private static byte[] PurchaseRequest(
        ulong price,
        ulong quantity,
        ulong offerId) =>
        Message(
            Unsigned(1, price),
            Unsigned(2, quantity),
            Unsigned(5, offerId));

    private static byte[] PurchaseOffer(
        ulong itemId,
        ulong offerId,
        params ulong[] prices) =>
        Message(
            Unsigned(1, itemId),
            Unsigned(2, offerId),
            Bytes(
                4,
                Packed(
                    [
                        (ulong)prices.Length,
                        .. prices
                    ])));

    private static byte[] PurchaseReceipt(
        ulong quantity,
        ulong offerId) =>
        Message(
            Unsigned(2, quantity),
            Unsigned(3, offerId));

    private sealed record NetworkCrushCapture(
        IReadOnlyList<NetworkCrushResultLine> Lines,
        DateTime ObservedAtUtc);

    private sealed class RoutingHarness
        : IAsyncDisposable
    {
        public const string Server =
            "TEST_SERVER";

        private readonly TestDatabase database;
        private readonly ServiceProvider provider;

        public RoutingHarness(
            TestDatabase database,
            ServiceProvider provider,
            CurrentServerState serverState,
            LastNetworkEquipmentState
                lastEquipment,
            WindowScopedNetworkEquipmentState windowEquipment,
            RoutingSettings settings,
            MarketDataChangeNotifier notifier,
            DofusNetworkCaptureService service,
            List<NetworkCrushCapture> crushes,
            List<MarketDataChangedEventArgs>
                notifications,
            EphemeralCandidateRosterGroupsState? rosterWorkspace = null)
        {
            this.database = database;
            this.provider = provider;

            ServerState = serverState;
            LastEquipment = lastEquipment;
            WindowEquipment = windowEquipment;
            Settings = settings;
            Notifier = notifier;
            Service = service;
            Crushes = crushes;
            Notifications = notifications;
            RosterWorkspace = rosterWorkspace;
        }

        public EphemeralCandidateRosterGroupsState? RosterWorkspace { get; }

        public CurrentServerState
            ServerState
        {
            get;
        }

        public LastNetworkEquipmentState
            LastEquipment
        {
            get;
        }

        public WindowScopedNetworkEquipmentState WindowEquipment { get; }

        public RoutingSettings
            Settings
        {
            get;
        }

        public MarketDataChangeNotifier
            Notifier
        {
            get;
        }

        public DofusNetworkCaptureService
            Service
        {
            get;
        }

        public List<NetworkCrushCapture>
            Crushes
        {
            get;
        }

        public List<MarketDataChangedEventArgs>
            Notifications
        {
            get;
        }

        public static async Task<RoutingHarness>
            CreateAsync(Func<CurrentServerState,
                EphemeralCandidateRosterGroupsState,
                EphemeralCandidateRosterWireObserver>? observerFactory = null)
        {
            TestDatabase database =
                new();

            ServiceCollection services =
                new();

            services.AddScoped(
                _ =>
                    database.CreateContext());

            services.AddSingleton<
                IDataPriorityProvider>(
                    new RoutingPriorityProvider());

            services.AddScoped<
                MarketPriceService>();
            services.AddSingleton<WindowScopedNetworkEquipmentState>();

            services.AddScoped<
                CoefficientService>();

            ServiceProvider provider =
                services
                    .BuildServiceProvider();

            using (
                BestCrushDbContext context =
                    database.CreateContext())
            {
                context.Equipments.AddRange(
                    new Equipment(42)
                    {
                        Name = "Equipment 42"
                    },
                    new Equipment(43)
                    {
                        Name = "Equipment 43"
                    });

                context.Runes.Add(
                    new Rune(100)
                    {
                        Name = "Rune 100"
                    });

                context.Resources.AddRange(
                    new Resource(200)
                    {
                        Name = "Resource 200"
                    },
                    new Resource(201)
                    {
                        Name = "Resource 201"
                    },
                    new Resource(202)
                    {
                        Name = "Resource 202"
                    });

                await context
                    .SaveChangesAsync();
            }

            CurrentServerState serverState =
                new();

            serverState.SelectServer(
                Server);

            LastNetworkEquipmentState lastEquipment =
                new();
            WindowScopedNetworkEquipmentState windowEquipment =
                provider.GetRequiredService<WindowScopedNetworkEquipmentState>();

            RoutingSettings settings =
                new();

            MarketDataChangeNotifier notifier =
                new();

            List<MarketDataChangedEventArgs>
                notifications = [];

            notifier.Changed +=
                (_, change) =>
                    notifications.Add(
                        change);

            List<NetworkCrushCapture>
                crushes = [];

            Mock<
                ILogger<DofusNetworkCaptureService>>
                logger = new();

            EphemeralCandidateRosterGroupsState? candidateWorkspace =
                observerFactory is null ? null : new();
            EphemeralCandidateRosterWireObserver? candidateObserver =
                candidateWorkspace is null ? null :
                observerFactory!(serverState, candidateWorkspace);

            DofusNetworkCaptureService service =
                new(
                    provider
                        .GetRequiredService<
                            IServiceScopeFactory>(),
                    serverState,
                    lastEquipment,
                    settings,
                    notifier,
                    (
                        lines,
                        observedAtUtc,
                        _,
                        _
                    ) =>
                    {
                        crushes.Add(
                            new NetworkCrushCapture(
                                lines.ToArray(),
                                observedAtUtc));

                        return Task.CompletedTask;
                    },
                    logger.Object,
                    rosterWireObserver: candidateObserver);

            string mapPath =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "NetworkFixtures",
                    "protocol-map.json");

            service.LoadProtocolMapForReplay(
                mapPath);

            return new RoutingHarness(
                database,
                provider,
                serverState,
                lastEquipment,
                windowEquipment,
                settings,
                notifier,
                service,
                crushes,
                notifications,
                candidateWorkspace);
        }

        public Task ReplayAsync(
            string key,
            byte[] body,
            DateTime observedAtUtc,
            long connectionId = 1,
            DofusClientWindowKey? candidateWindow = null) =>
            Service.ReplayMessageAsync(
                "TEST",
                key,
                body,
                observedAtUtc,
                connectionId: connectionId,
                candidateWindow: candidateWindow);

        public async Task<
            IReadOnlyList<
                MarketPriceObservation>>
            ReadPricesAsync(
                MarketObjectType objectType,
                long dofusDbId,
                string serverName = Server)
        {
            using BestCrushDbContext context =
                database.CreateContext();

            return await context
                .MarketPriceObservations
                .AsNoTracking()
                .Where(price =>
                    price.ObjectType ==
                        objectType &&
                    price.DofusDbId ==
                        dofusDbId &&
                    price.ServerName ==
                        serverName &&
                    !price.IsCleared)
                .OrderBy(price =>
                    price.Quantity)
                .ThenBy(price =>
                    price.ObservedAtUtc)
                .ToArrayAsync();
        }

        public async Task<
            IReadOnlyList<
                CoefficientObservation>>
            ReadCoefficientsAsync(
                long dofusDbId)
        {
            using BestCrushDbContext context =
                database.CreateContext();

            return await context
                .CoefficientObservations
                .AsNoTracking()
                .Where(coefficient =>
                    coefficient.DofusDbId ==
                        dofusDbId &&
                    coefficient.ServerName ==
                        Server &&
                    !coefficient.IsCleared)
                .OrderBy(coefficient =>
                    coefficient.ObservedAtUtc)
                .ToArrayAsync();
        }

        public async Task<
            CoefficientObservation?>
            ReadEffectiveCoefficientAsync(
                long dofusDbId)
        {
            using IServiceScope scope =
                provider.CreateScope();

            return await scope
                .ServiceProvider
                .GetRequiredService<
                    CoefficientService>()
                .GetLatestObservationAsync(
                    dofusDbId,
                    Server);
        }

        public ValueTask DisposeAsync()
        {
            Service.Dispose();
            provider.Dispose();
            database.Dispose();

            return ValueTask.CompletedTask;
        }
    }

    private sealed class
        RoutingSettings
        : IBestCrushSettingsProvider
    {
        public bool
            EquipmentCaptureEnabled
        {
            get;
            set;
        } = true;

        public bool
            RuneCaptureEnabled
        {
            get;
            set;
        } = true;

        public bool
            ResourceCaptureEnabled
        {
            get;
            set;
        } = true;

        public bool
            CoefficientCaptureEnabled
        {
            get;
            set;
        } = true;

        public CrushYieldEstimationMode
            CrushYieldEstimationMode
        {
            get;
            set;
        } =
            CrushYieldEstimationMode
                .Average;

        public double
            TargetRoiPercent
        {
            get;
            set;
        } = 15;

        public double
            CrushValueDiscountPercent
        {
            get;
            set;
        } = 5;
    }

    private sealed class
        RoutingPriorityProvider
        : IDataPriorityProvider
    {
        public DataPriority Priority
        {
            get;
            set;
        } =
            DataPriority
                .InGameAutomatic;
    }
}
