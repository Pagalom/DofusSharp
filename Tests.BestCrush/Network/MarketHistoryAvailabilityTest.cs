using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class MarketHistoryAvailabilityTest
{
    private static readonly DateTime At = new(2026, 11, 19, 11, 0, 0, DateTimeKind.Utc);
    private static byte[] Row(ulong id, ulong? price) => Bytes(3, price.HasValue
        ? Message(Unsigned(1, id), Unsigned(2, price.Value)) : Unsigned(1, id));
    private static byte[] Snapshot => Message(Row(33207, 373874), Row(111, null), Row(222, 0));

    [Fact]
    public void FullSnapshotDistinguishesPositiveOmittedAndZeroMeansWithoutInventingSales()
    {
        var decoded = MarketMeanPriceDecoder.Decode(Snapshot)!;
        Assert.Equal(3, decoded.EntryCount);
        Assert.Equal([33207L], decoded.WithMeanPrice);
        Assert.DoesNotContain(33215, decoded.WithMeanPrice);
        Assert.DoesNotContain(111, decoded.WithMeanPrice);
        Assert.DoesNotContain(222, decoded.WithMeanPrice);
    }

    [Fact]
    public void InvalidOrPartialFramesCannotBecomeEvidenceOfAbsentHistory()
    {
        byte[][] invalid = [[], Snapshot[..^1], Row(0, 1), Row(1, ulong.MaxValue),
            Message(Row(1, 1), Row(1, 2)), Bytes(3, Message(Unsigned(1, 1), Unsigned(1, 2))),
            Bytes(3, Message(Unsigned(1, 1), Unsigned(2, 1), Unsigned(2, 2))),
            Bytes(2, Unsigned(1, 1)), Bytes(3, Bytes(1, [1])), new byte[4 * 1024 * 1024 + 1]];
        foreach (var body in invalid) Assert.Null(MarketMeanPriceDecoder.Decode(body));
    }

    [Fact]
    public void NewMeanOrSuccessfulHistoryCanRestoreAvailabilityButATimerDoesNotInventIt()
    {
        var captured = new DateTimeOffset(At);
        var unavailable = new MarketHistoryAvailability(captured, new HashSet<long> { 33207 });
        Assert.True(unavailable.IsUnavailable(33215, null, captured));
        Assert.True(unavailable.IsUnavailable(33215, null, captured.AddMonths(1)));
        Assert.True(unavailable.IsUnavailable(33215, captured.AddSeconds(-1), captured));
        Assert.False(unavailable.IsUnavailable(33215, captured.AddSeconds(1), captured.AddSeconds(2)));
        Assert.False(unavailable.IsUnavailable(33207, null, captured));
        Assert.False((unavailable with { ObservedAtUtc = captured.AddDays(1) }).IsUnavailable(33215, null, captured));
        Assert.False((unavailable with { WithMeanPrice = new HashSet<long> { 33215 } }).IsUnavailable(33215, null, captured));
    }

    [Fact]
    public async Task OnlyServerMessagesOnAnActiveLeaseAndEnabledCaptureUpdateTheAvailabilityCache()
    {
        string directory = Path.Combine(Path.GetTempPath(), "history-availability-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "sales.db");
            var repository = new GlobalSalesRepository(path);
            var services = new ServiceCollection().AddSingleton(repository);
            using var provider = services.BuildServiceProvider();
            var state = new CurrentServerState(); state.SelectServer("Kourial");
            var settings = new Mock<IBestCrushSettingsProvider>();
            settings.SetupGet(s => s.ResourceCaptureEnabled).Returns(true);
            var debug = new NetworkDebugWriter(state, () => false, 5555);
            var writer = new NetworkObservationWriter(provider.GetRequiredService<IServiceScopeFactory>(), state,
                new LastNetworkEquipmentState(), settings.Object, new MarketDataChangeNotifier(), debug,
                (_, _, _, _) => Task.CompletedTask, NullLogger<DofusNetworkCaptureService>.Instance);
            var processor = new DofusNetworkMessageProcessor(writer, debug, state);
            var map = ProtocolMap.Load(Path.Combine(AppContext.BaseDirectory, "NetworkFixtures", "protocol-map.json"));
            Assert.Equal("isr", map.MarketMeanPrices);
            var lease = state.GetCaptureLease()!.Value;
            Task Send(string direction, byte[] body, DateTime at, NetworkCaptureLease? captured = null) =>
                processor.ProcessMessageAsync(map, new(direction, "isr", body, at, captured ?? lease, 1, ConnectionId: 1), default);
            await Send("C→S", Snapshot, At);
            Assert.Null(repository.ReadHistoryAvailability("Kourial", new(At)));
            settings.SetupGet(s => s.ResourceCaptureEnabled).Returns(false);
            await Send("S→C", Snapshot, At);
            Assert.Null(repository.ReadHistoryAvailability("Kourial", new(At)));
            settings.SetupGet(s => s.ResourceCaptureEnabled).Returns(true);
            await Send("S→C", Snapshot, At);
            var persisted = new GlobalSalesRepository(path).ReadHistoryAvailability("kourial", new(At))!;
            Assert.True(persisted.IsUnavailable(33215, null, new(At)));
            Assert.False(persisted.IsUnavailable(33207, null, new(At)));
            Assert.Empty(repository.ReadHistories("Kourial"));
            await Send("S→C", Row(33215, 999), At.AddMinutes(-1));
            Assert.True(repository.ReadHistoryAvailability("Kourial", new(At))!.IsUnavailable(33215, null, new(At)));
            await Send("S→C", Snapshot[..^1], At.AddSeconds(1));
            Assert.Equal(persisted.ObservedAtUtc, repository.ReadHistoryAvailability("Kourial", new(At.AddSeconds(1)))!.ObservedAtUtc);
            state.SelectServer("Tylezia");
            await Send("S→C", Row(33215, 999), At.AddSeconds(2));
            Assert.Null(repository.ReadHistoryAvailability("Tylezia", new(At.AddSeconds(2))));
            await Send("S→C", Row(33215, 999), At.AddSeconds(3), state.GetCaptureLease());
            Assert.False(repository.ReadHistoryAvailability("Tylezia", new(At.AddSeconds(3)))!.IsUnavailable(33215, null, new(At.AddSeconds(3))));
            Assert.True(repository.ReadHistoryAvailability("Kourial", new(At.AddSeconds(3)))!.IsUnavailable(33215, null, new(At.AddSeconds(3))));
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }
}
