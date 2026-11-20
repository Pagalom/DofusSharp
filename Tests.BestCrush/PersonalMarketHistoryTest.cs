using System.Text;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using BestCrush.Domain.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush;

public sealed class PersonalMarketHistoryTest
{
    [Theory]
    [InlineData("valid", 1)] [InlineData("wrong-direction", 0)] [InlineData("missing-window", 0)]
    [InlineData("other-window", 0)] [InlineData("revoked", 0)]
    public async Task NetworkRouteOnlyUpdatesTheMatchingPlayerOnAnActiveServer(string scenario, int count)
    {
        string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var clients = new CurrentDofusClientsState(); var window = new DofusClientWindowKey(10, 20);
            clients.Update([new DofusWindowInfo(new IntPtr(10), "Test - Iop - 3.7.14.18 - Release", "Dofus", 0, 0, 100, 100) { ProcessId = 20 }], window);
            var repository = new GlobalSalesRepository(Path.Combine(folder, "sales.db"));
            string scope = ProductionTrackingScope.For(clients, window, "server")!;
            repository.SaveListing("server", new(1, 100, 1, 100, []), Now.AddHours(-1), new(scope, 100, 42, 80, "normal", null));
            using var provider = new ServiceCollection().AddSingleton(clients).AddSingleton(repository).BuildServiceProvider();
            var state = new CurrentServerState(); state.SelectServer("server"); var lease = state.GetCaptureLease();
            var debug = new NetworkDebugWriter(state, () => false, 5555);
            var writer = new NetworkObservationWriter(provider.GetRequiredService<IServiceScopeFactory>(), state,
                new LastNetworkEquipmentState(), Mock.Of<IBestCrushSettingsProvider>(), new MarketDataChangeNotifier(), debug,
                (_, _, _, _) => Task.CompletedTask, NullLogger<DofusNetworkCaptureService>.Instance);
            var processor = new DofusNetworkMessageProcessor(writer, debug, state);
            var map = ProtocolMap.Load(Path.Combine(AppContext.BaseDirectory, "NetworkFixtures", "protocol-map.json"));
            if (scenario == "revoked") state.SelectServer("other");
            DofusClientWindowKey? actual = scenario == "missing-window" ? null : scenario == "other-window" ? new(30, 40) : window;
            for (int replay = 0; replay < 2; replay++)
                await processor.ProcessMessageAsync(map, new(scenario == "wrong-direction" ? "C→S" : "S→C", map.PersonalMarketHistory!, Row(100),
                    Now.UtcDateTime, lease, 1, ConnectionId: 1, CandidateWindow: actual), default);
            Assert.Equal(count, repository.ReadProductionSales("server", scope, Now).Count);
            Assert.Empty(repository.ReadProductionSales("other", scope, Now));
        }
        finally { Directory.Delete(folder, true); }
    }
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    [Fact]
    public void PersonalHistoryOpcodeAndNestedFieldsCanBeUpdatedInTheSharedProfile()
    {
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(file, """
                { "clientBuild":"test", "personal_market_history":"newhistory", "field_mappings":{
                "personal_market_history":{"1":{"wire":6,"fields":{"1":8,"3":9,"4":10,
                "5":{"wire":11,"fields":{"2":4,"3":7}}}}}}}
                """);
            var map = ProtocolMap.Load(file);
            var wire = Bytes(6, Message(Unsigned(8, 100), Bytes(9, Encoding.UTF8.GetBytes(Now.ToString("O"))),
                Bytes(11, Message(Unsigned(4, 100), Unsigned(7, 1)))));
            var row = Assert.Single(PersonalMarketHistoryDecoder.Decode(map.NormalizeBody(map.PersonalMarketHistory!, wire), Now)!);
            Assert.Equal(100, row.ItemId); Assert.Equal(100, row.Price); Assert.False(row.Returned);
        }
        finally { File.Delete(file); }
    }
    private static byte[] Row(long price, long item = 100, int quantity = 1, bool returned = false) => Bytes(1,
        Message(Unsigned(1, (ulong)price), Bytes(3, Encoding.UTF8.GetBytes(Now.AddMinutes(-1).ToString("O"))),
            returned ? Unsigned(4, 1) : [], Bytes(5, Message(Unsigned(2, (ulong)item), Unsigned(3, (ulong)quantity))), Unsigned(7, 1)));

    [Fact]
    public void DecodesPersonalSalesAndReturnsKeepsDuplicatesAndStableIdsAcrossReordering()
    {
        var body = Message(Row(100), Row(100), Row(200, returned: true), Row(300, quantity: 10));
        var rows = PersonalMarketHistoryDecoder.Decode(body, Now)!;
        Assert.Equal(4, rows.Count); Assert.Equal(3, rows.Count(r => !r.Returned));
        Assert.True(rows[2].Returned); Assert.Equal(10, rows[3].Quantity);
        Assert.Equal(4, rows.Select(r => r.Id).Distinct().Count());
        var reordered = PersonalMarketHistoryDecoder.Decode(Message(Row(200, returned: true), Row(100), Row(300, quantity: 10), Row(100)), Now)!;
        Assert.Equal(rows.Select(r => r.Id).Order(), reordered.Select(r => r.Id).Order());
        Assert.Null(PersonalMarketHistoryDecoder.Decode(body, Now.AddDays(-1)));
        Assert.Null(PersonalMarketHistoryDecoder.Decode(Message(Enumerable.Repeat(Row(1), 1001).ToArray()), Now));
    }

    [Fact]
    public void MatchesThePriceDateLevelAndVariantWithoutCountingReturnsOrReplaysAsSales()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            var repository = new GlobalSalesRepository(path);
            var evidence = new ProductionListingEvidence("a", 100, 42, 80, "normal", null);
            repository.SaveListing("server", new(1, 100, 1, 100, []), Now.AddHours(-1), evidence);
            var history = PersonalMarketHistoryDecoder.Decode(Message(Row(100, returned: true)), Now)!;
            Assert.Equal(1, repository.MatchProductionHistory("server", "a", history, Now));
            Assert.Empty(repository.ReadProductionSales("server", "a", Now));
            Assert.Single(repository.ReadProductionReturns("server", "a", Now));
            repository.SaveListing("server", new(2, 100, 1, 100, []), Now.AddMinutes(-30), evidence with { InstanceUid = 43 });
            history = PersonalMarketHistoryDecoder.Decode(Message(Row(100)), Now)!;
            Assert.Equal(0, repository.MatchProductionHistory("server", "b", history, Now));
            Assert.Equal(1, repository.MatchProductionHistory("server", "a", history, Now));
            repository.SaveListing("server", new(3, 100, 1, 100, []), Now.AddMinutes(-20), evidence with { InstanceUid = 44 });
            Assert.Equal(0, repository.MatchProductionHistory("server", "a", history, Now));
            Assert.Single(repository.ReadProductionSales("server", "a", Now));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingEffectsCannotChooseBetweenDifferentLevelsOrSpecialPowers()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            var repository = new GlobalSalesRepository(path);
            repository.SaveListing("server", new(1, 100, 1, 100, []), Now.AddHours(-1), new("a", 100, 42, 80, "normal", null));
            repository.SaveListing("server", new(2, 100, 1, 100, []), Now.AddHours(-1), new("a", 100, 43, 100, "legendary", null));
            var history = PersonalMarketHistoryDecoder.Decode(Message(Row(100)), Now)!;
            Assert.Equal(0, repository.MatchProductionHistory("server", "a", history, Now));
            var effects = new[] { Message(Unsigned(10, 2821), Bytes(9, Message(Unsigned(1, 18), Unsigned(4, 80)))) };
            Assert.Equal(1, repository.MatchProductionHistory("server", "a", [history[0] with { Effects = effects }], Now));
            Assert.Equal("1", Assert.Single(repository.ReadProductionSales("server", "a", Now)).ListingId);
        }
        finally { File.Delete(path); }
    }
}
