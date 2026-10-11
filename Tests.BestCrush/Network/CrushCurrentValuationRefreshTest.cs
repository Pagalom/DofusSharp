using System.Collections;
using System.Reflection;
using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tests.BestCrush.Utils;

namespace Tests.BestCrush.Network;

/// <summary>
/// P4.1: exercises the real singleton's market-notification subscription
/// and scoped SQLite valuation, without instantiating Maui windows.
/// </summary>
public sealed class CrushCurrentValuationRefreshTest
{
    private const string Server = "TEST_SERVER";
    private static readonly DateTime BaseTime =
        new(2026, 10, 11, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PriceNotificationRevaluesLatestResultAndItsLotsAndDiscount()
    {
        using Harness harness = new();
        await harness.StoreAsync(1523, 10, 1595, at: BaseTime);
        await harness.StoreAsync(1523, 1, 130, at: BaseTime);
        harness.SeedNetworkCrush(1523, 16, currentValue: 2375);

        await harness.StoreAsync(
            1523, 10, 1700, at: BaseTime.AddMinutes(1));
        await harness.StoreAsync(
            1523, 1, 140, at: BaseTime.AddMinutes(1));
        harness.Notifier.Notify(MarketObjectType.Rune, 1523, Server, 10);
        await harness.Service.LastValuationRefreshForTests;

        CrushSessionSnapshot snapshot = harness.Snapshot();
        snapshot.NetworkCrushCount.Should().Be(1);
        snapshot.Runes.Should().ContainSingle();
        snapshot.Runes.Single().Quantity.Should().Be(16);
        snapshot.Runes.Single().Value.Should().Be(2540);
        snapshot.TotalValue.Should().Be(2540);
        snapshot.DiscountedTotalValue.Should().Be(2413);
        var lots = snapshot.Runes.Single().Lots;
        lots.Should().HaveCount(2);
        lots[0].Count.Should().Be(1);
        lots[0].LotQuantity.Should().Be(10);
        lots[0].LotPrice.Should().Be(1700);
        lots[1].Count.Should().Be(6);
        lots[1].LotQuantity.Should().Be(1);
        lots[1].LotPrice.Should().Be(140);
    }

    [Fact]
    public async Task WrongRuneObjectTypeOrServerLeavesSnapshotUntouched()
    {
        using Harness harness = new();
        await harness.StoreAsync(1523, 1, 200);
        harness.SeedNetworkCrush(1523, 16, currentValue: 2375);

        harness.Notifier.Notify(
            MarketObjectType.Equipment, 1523, Server);
        harness.Notifier.Notify(
            MarketObjectType.Resource, 1523, Server);
        harness.Notifier.Notify(
            MarketObjectType.Rune, 9999, Server);
        harness.Notifier.Notify(
            MarketObjectType.Rune, 1523, "OTHER_SERVER");

        harness.Snapshot().TotalValue.Should().Be(2375);
        harness.Service.LastValuationRefreshForTests.IsCompleted
            .Should().BeTrue();
    }

    [Fact]
    public async Task ServerSelectionChangePreventsRevaluationOfPreviousServerResult()
    {
        using Harness harness = new();
        await harness.StoreAsync(1523, 1, 200);
        harness.SeedNetworkCrush(1523, 16, currentValue: 2375);

        harness.ServerState.SelectServer("OTHER_SERVER");
        harness.Notifier.Notify(MarketObjectType.Rune, 1523, Server);

        harness.Snapshot().TotalValue.Should().Be(2375);
        harness.Service.LastValuationRefreshForTests.IsCompleted
            .Should().BeTrue();
    }

    [Fact]
    public async Task ClosingResultPreventsLaterPriceNotificationsFromRestoringIt()
    {
        using Harness harness = new();
        await harness.StoreAsync(1523, 1, 200);
        harness.SeedNetworkCrush(1523, 16, currentValue: 2375);

        harness.Service.CloseAndReset();
        harness.Notifier.Notify(MarketObjectType.Rune, 1523, Server);

        CrushSessionSnapshot snapshot = harness.Snapshot();
        snapshot.Runes.Should().BeEmpty();
        snapshot.TotalValue.Should().BeNull();
        snapshot.NetworkCrushCount.Should().Be(0);
    }

    [Fact]
    public async Task MissingPricesLeaveLatestResultIncompleteNotStale()
    {
        using Harness harness = new();
        harness.SeedNetworkCrush(1523, 16, currentValue: 2375);
        harness.Notifier.Notify(MarketObjectType.Rune, 1523, Server);

        await harness.Service.LastValuationRefreshForTests;

        CrushSessionSnapshot snapshot = harness.Snapshot();
        snapshot.Runes.Should().ContainSingle()
            .Which.Value.Should().BeNull();
        snapshot.Runes.Single().Lots.Should().BeEmpty();
        snapshot.TotalValue.Should().BeNull();
        snapshot.DiscountedTotalValue.Should().BeNull();
    }

    [Fact]
    public async Task RealMarketPriorityStillControlsTheRefreshedPrice()
    {
        using Harness harness = new();
        harness.Priority.Priority = DataPriority.Manual;
        await harness.StoreAsync(
            1523, 10, 2222, MarketPriceSource.Manual, BaseTime);
        await harness.StoreAsync(
            1523, 10, 3000, MarketPriceSource.InGameAutomatic,
            BaseTime.AddMinutes(2));
        await harness.StoreAsync(
            1523, 1, 130, MarketPriceSource.InGameAutomatic, BaseTime);
        harness.SeedNetworkCrush(1523, 16, currentValue: 2375);

        harness.Notifier.Notify(MarketObjectType.Rune, 1523, Server);
        await harness.Service.LastValuationRefreshForTests;

        harness.Snapshot().TotalValue.Should().Be(3002);
    }

    private sealed class Harness : IDisposable
    {
        private readonly TestDatabase database = new();
        private readonly ServiceProvider provider;
        public readonly MarketDataChangeNotifier Notifier = new();
        public readonly CurrentServerState ServerState = new();
        public readonly TestDataPriorityProvider Priority = new();
        public readonly CrushSessionService Service;

        public Harness()
        {
            ServerState.SelectServer(Server);
            ServiceCollection services = new();
            services.AddScoped(_ => database.CreateContext());
            services.AddScoped<MarketPriceService>();
            services.AddSingleton<IDataPriorityProvider>(Priority);
            provider = services.BuildServiceProvider();

            // Layout and control-bar services are never touched by these
            // in-memory snapshot tests: no app/window is ever opened.
            Service = new CrushSessionService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                null!,
                null!,
                Mock.Of<IBestCrushSettingsProvider>(),
                Notifier,
                ServerState,
                NullLogger<CrushSessionService>.Instance);
        }

        public void SeedNetworkCrush(
            long runeId, int quantity, double currentValue)
        {
            SetField("_sessionServerName", Server);
            SetField("_sessionId", 1L);
            SetField("_networkCrushCount", 1);
            SetField("_sessionDiscountPercent", 5d);

            // Seed ONLY the existing private state, not any UI or database.
            // A P3 network result normally populates these fields.
            Type runeType = typeof(CrushSessionService).GetNestedType(
                "AccumulatedRune", BindingFlags.NonPublic)!;
            object rune = Activator.CreateInstance(runeType, nonPublic: true)!;
            runeType.GetProperty("Name")!.SetValue(rune, "Rune Vi");
            runeType.GetProperty("Quantity")!.SetValue(rune, quantity);
            runeType.GetProperty("Value")!.SetValue(rune, currentValue);

            IDictionary runes = (IDictionary)GetField("_runes").GetValue(Service)!;
            runes.Add(runeId, rune);
        }

        public async Task StoreAsync(
            long runeId, int quantity, long price,
            MarketPriceSource source = MarketPriceSource.InGameAutomatic,
            DateTime? at = null)
        {
            using BestCrushDbContext context = database.CreateContext();
            context.MarketPriceObservations.Add(new MarketPriceObservation
            {
                ObjectType = MarketObjectType.Rune,
                DofusDbId = runeId,
                ServerName = Server,
                Price = price,
                Quantity = quantity,
                Source = source,
                ObservedAtUtc = at ?? BaseTime
            });
            await context.SaveChangesAsync();
        }

        public CrushSessionSnapshot Snapshot()
        {
            MethodInfo method = typeof(CrushSessionService).GetMethod(
                "CreateSnapshot",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (CrushSessionSnapshot)method.Invoke(Service, null)!;
        }

        private void SetField(string name, object value) =>
            GetField(name).SetValue(Service, value);

        private static FieldInfo GetField(string name) =>
            typeof(CrushSessionService).GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic)!;

        public void Dispose()
        {
            provider.Dispose();
            database.Dispose();
        }
    }
}
