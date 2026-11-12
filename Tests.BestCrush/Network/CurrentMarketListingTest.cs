using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class CurrentMarketListingTest
{
    // Actual request and confirmation: Conque marine x10 at 910 K, 2026-11-11 00:21:27 UTC.
    private static byte[] Request => Convert.FromHexString("08c88852100a188e07");
    private static byte[] Confirmation => Convert.FromHexString("088e071209089e6b18d5ef0a200a1880d49301");
    private static byte[] Created(ulong quantity = 10, ulong price = 910) =>
        Message(Unsigned(1, price), Bytes(2, Message(Unsigned(1, 13726), Unsigned(3, 178133), Unsigned(4, quantity))), Unsigned(3, 2419200));

    [Fact]
    public void ActualCapturedMessagesDecodeTheLotTotalAndKeepTheTwoUidKindsDistinct()
    {
        var map = ProtocolMap.Load(Path.Combine(AppContext.BaseDirectory, "NetworkFixtures", "protocol-map.json"));
        Assert.Equal(new MarketListingRequestObservation(1344584, 10, 910),
            MarketListingDecoder.DecodeRequest(map.NormalizeBody(map.CurrentMarketListingRequest!, Request)));
        var listing = Assert.IsType<MarketListingCreatedObservation>(
            MarketListingDecoder.DecodeCreated(map.NormalizeBody(map.CurrentMarketListingCreated!, Confirmation)));
        Assert.Equal(178133UL, listing.MarketListingUid);
        Assert.Equal(13726UL, listing.ItemId);
        Assert.Equal(10UL, listing.Quantity);
        Assert.Equal(910UL, listing.Price);
        Assert.Empty(listing.Stats);
    }

    [Fact]
    public void MalformedAmbiguousMissingAndOverflowFieldsCannotProduceListings()
    {
        byte[][] invalid = [[], Confirmation[..^1], [0x12, 0xff, 0xff, 0xff, 0xff, 0x07],
            Message(Created(), Unsigned(1, 910)),
            Message(Unsigned(1, 910), Bytes(2, Message(Unsigned(1, 13726), Unsigned(3, 178133)))),
            Created(0), Created(ulong.MaxValue), Created(price: 0), Created(price: ulong.MaxValue),
            Message(Unsigned(1, 910), Bytes(2, Message(Unsigned(1, 13726), Unsigned(1, 13727),
                Unsigned(3, 178133), Unsigned(4, 10))))];
        foreach (var body in invalid) Assert.Null(MarketListingDecoder.DecodeCreated(body));
        Assert.Null(MarketListingDecoder.DecodeRequest(Request[..^1]));
        Assert.Null(MarketListingDecoder.DecodeRequest(Message(Request, Unsigned(2, 10))));
        Assert.Null(MarketListingDecoder.DecodeRequest(Message(Unsigned(1, 1344584), Unsigned(2, ulong.MaxValue), Unsigned(3, 910))));
    }

    [Theory]
    [InlineData("valid", 10)]
    [InlineData("unsolicited", 0)]
    [InlineData("request-wrong-direction", 0)]
    [InlineData("confirmation-wrong-direction", 0)]
    [InlineData("other-connection", 0)]
    [InlineData("expired", 0)]
    [InlineData("future-request", 0)]
    [InlineData("different-price", 0)]
    [InlineData("different-quantity", 0)]
    [InlineData("revoked-lease", 0)]
    public async Task ProcessorOnlyCountsOneMatchingOwnConfirmationOnTheActiveFlow(string scenario, long units)
    {
        string folder = Path.Combine(Path.GetTempPath(), "current-listing-" + Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new GlobalSalesRepository(Path.Combine(folder, "sales.db"));
            using var provider = new ServiceCollection().AddSingleton(repository).BuildServiceProvider();
            var server = new CurrentServerState();
            server.SelectServer("Tylezia");
            var lease = server.GetCaptureLease();
            var debug = new NetworkDebugWriter(server, () => false, 5555);
            var writer = new NetworkObservationWriter(provider.GetRequiredService<IServiceScopeFactory>(), server,
                new LastNetworkEquipmentState(), new Mock<IBestCrushSettingsProvider>().Object,
                new MarketDataChangeNotifier(), debug, (_, _, _, _) => Task.CompletedTask,
                NullLogger<DofusNetworkCaptureService>.Instance);
            var processor = new DofusNetworkMessageProcessor(writer, debug, server);
            var map = ProtocolMap.Load(Path.Combine(AppContext.BaseDirectory, "NetworkFixtures", "protocol-map.json"));
            DateTime at = new(2026, 11, 11, 0, 21, 27, DateTimeKind.Utc);
            Task Send(string key, byte[] body, string direction, DateTime time, long connection = 1) =>
                processor.ProcessMessageAsync(map, new(direction, key, body, time, lease, 1, ConnectionId: connection), default);
            int changed = 0;
            repository.ListingsChanged += _ => changed++;
            if (scenario != "unsolicited")
                await Send(map.CurrentMarketListingRequest!, Request, scenario == "request-wrong-direction" ? "S→C" : "C→S", at);
            if (scenario == "revoked-lease")
                server.SelectServer("Kourial");
            var response = scenario switch
            {
                "different-price" => Created(price: 911),
                "different-quantity" => Created(quantity: 100),
                _ => Confirmation
            };
            DateTime receivedAt = at.AddSeconds(scenario == "expired" ? 16 : scenario == "future-request" ? -1 : 1);
            for (int duplicate = 0; duplicate < 2; duplicate++)
                await Send(map.CurrentMarketListingCreated!, response, scenario == "confirmation-wrong-direction" ? "C→S" : "S→C",
                    receivedAt, scenario == "other-connection" ? 2 : 1);
            var totals = repository.ReadListingTotals("Tylezia", new DateTimeOffset(at.AddSeconds(30)));
            Assert.Equal(units, totals.ByItem.GetValueOrDefault(13726));
            Assert.Equal(units > 0 ? 1 : 0, changed);
            Assert.Empty(repository.ReadListingTotals("Kourial", new DateTimeOffset(at.AddSeconds(30))).ByItem);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
