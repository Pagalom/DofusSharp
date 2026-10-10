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
            RoutingSettings settings,
            MarketDataChangeNotifier notifier,
            DofusNetworkCaptureService service,
            List<NetworkCrushCapture> crushes,
            List<MarketDataChangedEventArgs>
                notifications)
        {
            this.database = database;
            this.provider = provider;

            ServerState = serverState;
            LastEquipment = lastEquipment;
            Settings = settings;
            Notifier = notifier;
            Service = service;
            Crushes = crushes;
            Notifications = notifications;
        }

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
            CreateAsync()
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
                    logger.Object);

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
                settings,
                notifier,
                service,
                crushes,
                notifications);
        }

        public Task ReplayAsync(
            string key,
            byte[] body,
            DateTime observedAtUtc) =>
            Service.ReplayMessageAsync(
                "TEST",
                key,
                body,
                observedAtUtc);

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
