using System.Text;
using BestCrush.Domain.Models;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tests.BestCrush.Utils;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class GlobalMarketHistoryTest
{
    private static readonly DateTime At = new(2026, 10, 28, 11, 10, 0, DateTimeKind.Utc);
    private static byte[] Fixture(long id) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
        "NetworkFixtures", $"global-market-{id}.bin"));
    private static byte[] Point(ulong item = 881, ulong price = 171, ulong units = 5) => Message(
        Unsigned(1, price), Unsigned(2, item), Bytes(3, Encoding.UTF8.GetBytes("2026-10-28T11:00:00.123456789Z")), Unsigned(4, units));

    [Theory]
    [InlineData(881, 7471, 32106, 103557)]
    [InlineData(30701, 51, 0, 2270)]
    public void CapturedResourceAndEquipmentMatchVisibleGlobalSales(long id, long day, long week, long month)
    {
        var decoded = GlobalMarketHistoryDecoder.DecodeResponse(Fixture(id), id, new(At));
        Assert.NotNull(decoded);
        Assert.Equal(day, decoded.Last24Hours.Sum(x => x.UnitsSold));
        Assert.Equal(month, decoded.Last30Days.Sum(x => x.UnitsSold));
        var history = new GlobalSalesHistory(new("Tylezia", id == 881 ? MarketObjectType.Resource : MarketObjectType.Equipment, id), new(At),
            decoded.Last24Hours.Select(x => new GlobalSalePoint(x.UnitPrice, x.UnitsSold, x.RecordedAtUtc)).ToArray(),
            decoded.Last30Days.Select(x => new GlobalSalePoint(x.UnitPrice, x.UnitsSold, x.RecordedAtUtc)).ToArray());
        if (week > 0) Assert.Equal(week, history.Period(7).UnitsSold);
        Assert.Equal(month, history.Period(30).UnitsSold);
        Assert.Equal(day, history.Period(1).UnitsSold);
        Assert.InRange(history.WeightedUnitPrice(1)!.Value, id == 881 ? 174d : 77d, id == 881 ? 175d : 78d);
    }

    [Fact]
    public void RejectsMalformedForeignDuplicatedAndOversizedHistory()
    {
        Assert.Null(GlobalMarketHistoryDecoder.DecodeResponse(Fixture(881), 30701, new(At)));
        Assert.Null(GlobalMarketHistoryDecoder.DecodeResponse(Fixture(881)[..^1], 881, new(At)));
        Assert.Null(GlobalMarketHistoryDecoder.DecodeResponse([], 881, new(At)));
        Assert.Null(GlobalMarketHistoryDecoder.DecodeResponse(new byte[256 * 1024 + 1], 881, new(At)));
        Assert.Null(GlobalMarketHistoryDecoder.DecodeResponse(Bytes(2, Point()), 881, new(At)));
        Assert.Null(GlobalMarketHistoryDecoder.DecodeResponse(Message(Bytes(1, Point()), Bytes(1, Point())), 881, new(At)));
        Assert.Null(GlobalMarketHistoryDecoder.DecodeResponse(Bytes(1, Point(units: ulong.MaxValue)), 881, new(At)));
        Assert.Null(GlobalMarketHistoryDecoder.DecodeResponse(Bytes(1, Point()), 881, new(At.AddDays(-1))));
        var extra = Message(Point(), Unsigned(5, 9));
        Assert.Null(GlobalMarketHistoryDecoder.DecodeResponse(Bytes(1, extra), 881, new(At)));
    }

    [Fact]
    public void RequestHasExactlyOneItemAndKnownZeroIsPreserved()
    {
        Assert.Equal(881, GlobalMarketHistoryDecoder.DecodeRequest(Unsigned(1, 881)));
        Assert.Null(GlobalMarketHistoryDecoder.DecodeRequest([]));
        Assert.Null(GlobalMarketHistoryDecoder.DecodeRequest([0x0a,0xff,0xff,0xff,0xff,0x07]));
        Assert.Null(GlobalMarketHistoryDecoder.DecodeRequest(Unsigned(1, 0)));
        Assert.Null(GlobalMarketHistoryDecoder.DecodeRequest(Message(Unsigned(1, 881), Unsigned(1, 999))));
        var decoded = GlobalMarketHistoryDecoder.DecodeResponse(Bytes(3, Point(units: 0)), 881, new(At));
        Assert.NotNull(decoded);
        Assert.Equal(0, decoded.Last30Days.Single().UnitsSold);
    }

    [Fact]
    public async Task OnlyMatchedSameFlowActiveLeaseServerResponsePersistsAndReconsultingDoesNotDoubleCount()
    {
        using var database = new TestDatabase();
        using (var seed = database.CreateContext())
        { seed.Resources.Add(new Resource(881) { Name = "Laine" }); await seed.SaveChangesAsync(); }
        string path = Path.Combine(Path.GetTempPath(), "global-market-test-" + Guid.NewGuid(), "sales.db");
        var repository = new GlobalSalesRepository(path);
        var services = new ServiceCollection();
        services.AddScoped(_ => database.CreateContext());
        services.AddSingleton(repository);
        using var provider = services.BuildServiceProvider();
        var state = new CurrentServerState(); state.SelectServer("Tylezia");
        var notifier = new MarketDataChangeNotifier();
        var settings = new Mock<global::BestCrush.Domain.Services.IBestCrushSettingsProvider>();
        settings.SetupGet(x => x.ResourceCaptureEnabled).Returns(true);
        var debug = new NetworkDebugWriter(state, () => false, 5555);
        var writer = new NetworkObservationWriter(provider.GetRequiredService<IServiceScopeFactory>(), state,
            new LastNetworkEquipmentState(), settings.Object, notifier, debug, (_, _, _, _) => Task.CompletedTask,
            NullLogger<DofusNetworkCaptureService>.Instance);
        var processor = new DofusNetworkMessageProcessor(writer, debug, state);
        var map = ProtocolMap.Load(Path.Combine(AppContext.BaseDirectory, "NetworkFixtures", "protocol-map.json"));
        Assert.Equal("iqp", map.GlobalMarketHistoryRequest); Assert.Equal("ire", map.GlobalMarketHistoryResponse);
        var lease = state.GetCaptureLease();
        Task Send(string direction, string key, byte[] body, long connection = 1, DateTime? at = null,
            long epoch = 1, NetworkCaptureLease? scope = null) => processor.ProcessMessageAsync(map,
                new(direction, key, body, at ?? At, scope ?? lease, epoch, ConnectionId: connection), default);
        await Send("S→C", "ire", Fixture(881));
        Assert.Empty(repository.ReadHistories("Tylezia"));
        await Send("C→S", "iqp", Unsigned(1, 881));
        await Send("S→C", "ire", Fixture(881), connection: 2);
        Assert.Empty(repository.ReadHistories("Tylezia"));
        await Send("S→C", "ire", Fixture(881), at: At.AddSeconds(16));
        Assert.Empty(repository.ReadHistories("Tylezia"));
        await Send("C→S", "iqp", Unsigned(1, 881));
        await Send("S→C", "ire", Fixture(881), epoch: 2);
        Assert.Empty(repository.ReadHistories("Tylezia"));
        await Send("C→S", "iqp", Unsigned(1, 881), epoch: 2);
        await Send("S→C", "ire", Fixture(881), epoch: 2);
        Assert.Equal(7471, repository.ReadHistories("Tylezia").Single().Period(1).UnitsSold);
        Assert.Equal(103557, repository.ReadHistories("Tylezia").Single().Period(30).UnitsSold);
        await Send("C→S", "iqp", Unsigned(1, 881), epoch: 2);
        await Send("S→C", "ire", Fixture(881), epoch: 2);
        Assert.Single(repository.ReadHistories("Tylezia"));
        Assert.Equal(103557, repository.ReadHistories("Tylezia").Single().Period(30).UnitsSold);
        state.SelectServer("Kourial");
        await Send("C→S", "iqp", Unsigned(1, 881), epoch: 2);
        await Send("S→C", "ire", Fixture(881), epoch: 2);
        Assert.Empty(repository.ReadHistories("Kourial"));
        // A different valid lease begins with no request inherited from Tylezia.
        lease = state.GetCaptureLease();
        await Send("S→C", "ire", Fixture(881), epoch: 2);
        Assert.Empty(repository.ReadHistories("Kourial"));
    }
}
