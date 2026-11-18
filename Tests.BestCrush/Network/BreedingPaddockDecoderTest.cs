using System.Text;
using System.Net;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class BreedingPaddockDecoderTest
{
    private static readonly Guid MountId = Guid.Parse("4f31d816-8f4a-4893-b50f-61cd62143a98");

    [Fact]
    public void ReadsActiveGaugesFuelAndMountIdentityAcrossUpdates()
    {
        var before = BreedingPaddockDecoder.Decode(Snapshot(Mount(2_580)));
        var after = BreedingPaddockDecoder.Decode(Snapshot(Mount(2_570)));

        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal(
            [PaddockGauge.SerenityLoss, PaddockGauge.Maturity],
            before.Gauges.Where(reading => reading.Active).Select(reading => reading.Gauge));
        Assert.Equal(69_980, before.Gauges.Single(reading => reading.Gauge == PaddockGauge.Maturity).Fuel);
        var mount = Assert.Single(before.Mounts);
        Assert.Equal(new(MountId, 96, 160_000, 2_580, 8_020), mount);
        Assert.Equal(mount with { Serenity = 2_570 }, Assert.Single(after.Mounts));
    }

    [Fact]
    public void ReadsAnEmptyEnclosureAndAnAbsentFuelAmount()
    {
        var result = BreedingPaddockDecoder.Decode(Snapshot());

        Assert.NotNull(result);
        Assert.Empty(result.Mounts);
        Assert.Equal(0, result.Gauges.Single(reading => reading.Gauge == PaddockGauge.Experience).Fuel);
    }

    [Fact]
    public void ReadsNegativeSerenity()
    {
        var result = BreedingPaddockDecoder.Decode(Snapshot(Mount(-2_001)));

        Assert.NotNull(result);
        Assert.Equal(-2_001, Assert.Single(result.Mounts).Serenity);
    }

    [Fact]
    public void ReadsLoveAndEnduranceIncludingExplicitZero()
    {
        var mount = Bytes(5, Message(Id(), Bytes(2, Message(
            Bytes(6, Unsigned(2, 690)),
            Bytes(6, Unsigned(1, 2)),
            Bytes(6, Message(Unsigned(1, 1), Unsigned(2, 70))),
            Unsigned(8, 1), Unsigned(12, 10)))));
        var result = BreedingPaddockDecoder.Decode(Snapshot(mount));
        var stats = Assert.Single(result!.Mounts);
        Assert.Equal(690, stats.Love);
        Assert.Equal(0, stats.Endurance);
        Assert.Equal(10, stats.SpeciesId);
        Assert.Equal(70, stats.Maturity);
    }

    [Theory]
    [InlineData(1, 0, false, BreedingMountSex.Male, BreedingMountFertility.Fertile)]
    [InlineData(1, 1, true, BreedingMountSex.Male, BreedingMountFertility.Sterile)]
    [InlineData(0, 0, true, BreedingMountSex.Female, BreedingMountFertility.Ready)]
    public void ReadsStableSexAndFertility(int sex, int status, bool full,
        BreedingMountSex expectedSex, BreedingMountFertility expectedFertility)
    {
        ulong points = full ? 20_000UL : 0;
        var mount = Bytes(1, Message(Id(), Bytes(2, Message(
            sex == 1 ? Unsigned(2, 1) : [], status == 1 ? Unsigned(10, 1) : [], Unsigned(8, 1), Unsigned(12, 20),
            Bytes(6, Unsigned(2, points)),
            Bytes(6, Message(Unsigned(1, 1), Unsigned(2, points))),
            Bytes(6, Message(Unsigned(1, 2), Unsigned(2, points)))))));
        var stats = Assert.Single(BreedingPaddockDecoder.DecodeStable(Bytes(1, mount))!);
        Assert.Equal(expectedSex, stats.Sex);
        Assert.Equal(expectedFertility, stats.Fertility);
    }

    [Fact]
    public void RejectsIncompleteSnapshotsAndRepeatedMountIds()
    {
        Assert.Null(BreedingPaddockDecoder.Decode(Bytes(1, [])));
        Assert.Null(BreedingPaddockDecoder.Decode(Snapshot(Mount(0), Mount(0))));
    }

    [Fact]
    public void ReadsOpcodeFromTheVersionedMap()
    {
        var map = ProtocolMap.Load(Path.Combine(AppContext.BaseDirectory, "NetworkFixtures", "protocol-map.json"));

        Assert.Equal("hrf", map.CurrentBreedingPaddockSnapshot);
    }

    [Fact]
    public void LiveReadingBelongsOnlyToItsCurrentGameWindow()
    {
        var activeWindow = new DofusClientWindowKey(101, 1201);
        var otherWindow = new DofusClientWindowKey(102, 1202);
        var socket = new TcpSocketTuple(IPAddress.Parse("192.0.2.10"), 48001,
            IPAddress.Parse("198.51.100.20"), 5555);
        var bindings = new LiveDofusClientNetworkBindings();
        Assert.True(bindings.Observe(socket, 1, [new(socket, 1201)], [activeWindow, otherWindow]));
        Assert.Equal(activeWindow, bindings.ResolveConnection(socket, 1, 2));
        var service = new LiveBreedingService(bindings);
        var map = ProtocolMap.Load(Path.Combine(AppContext.BaseDirectory, "NetworkFixtures", "protocol-map.json"));
        var now = new DateTime(2026, 11, 15, 23, 50, 0, DateTimeKind.Utc);
        var reading = new DofusWireMessage("S→C", "hrf", Snapshot(Mount(2_380)), now,
            null, 1, ConnectionId: 2, CandidateWindow: activeWindow);

        service.Observe(map, reading);
        Assert.Single(service.ForWindow(activeWindow, now)!.Reading.Mounts);
        Assert.Null(service.ForWindow(otherWindow, now));
        Assert.Null(service.ForWindow(activeWindow, now.AddSeconds(31)));

        Assert.Equal(activeWindow, bindings.ResolveConnection(socket, 1, 3));
        service.Observe(map, reading with { ObservedAtUtc = now.AddSeconds(1) });
        Assert.Null(service.ForWindow(activeWindow, now.AddSeconds(1)));
    }

    [Fact]
    public void StableSnapshotReadsExistingMountsAndAnEmptyRoster()
    {
        var mounting = Mount(990, 1);
        var occupants = BreedingPaddockDecoder.DecodeStable(Bytes(1, Message(mounting,
            Bytes(3, Message(Unsigned(1, 1), Unsigned(2, 8))))));

        Assert.Equal(new(MountId, 96, 160_000, 990, 8_020), Assert.Single(occupants!));
        Assert.Empty(BreedingPaddockDecoder.DecodeStable(Bytes(1, []))!);
        Assert.Null(BreedingPaddockDecoder.DecodeStable(Bytes(9, [])));
    }

    [Fact]
    public void StableUpdatesKeepTheSameMountNumberAndDoNotDuplicateIt()
    {
        var fixture = new LiveFixture();
        fixture.Send("hot", Unsigned(1, 1), "C→S");
        fixture.Send("hpa", Selected(Mount(990)));
        fixture.Send("hqv", Bytes(1, []));
        int number = fixture.View.MountNumbers[MountId];

        fixture.Send("hqy", Transfer(0));
        Assert.Empty(Assert.Single(fixture.View.Enclosures).Reading.Mounts);
        Assert.Equal(MountId, Assert.Single(fixture.View.Stable).Id);
        Assert.Equal(990, fixture.View.Stable[0].Reading!.Serenity);
        Assert.Equal(number, fixture.View.MountNumbers[MountId]);

        fixture.Send("hqy", Transfer(0));
        Assert.Single(fixture.View.Stable);
        fixture.Send("hqk", Bytes(1, Transfer(3)));
        Assert.Single(fixture.View.Stable);
        fixture.Send("hqk", Bytes(1, Transfer(0)));
        fixture.Send("hrf", Snapshot(Mount(990)));
        Assert.Empty(fixture.View.Stable);
        Assert.Equal(MountId, Assert.Single(Assert.Single(fixture.View.Enclosures).Reading.Mounts).Id);
        Assert.Equal(number, fixture.View.MountNumbers[MountId]);
    }

    [Fact]
    public void SelectedEnclosuresAreKeptSeparatelyAndPeriodicUpdatesUseTheCurrentSelection()
    {
        var fixture = new LiveFixture();
        fixture.Send("hot", Unsigned(1, 1), "C→S");
        fixture.Send("hpa", Selected(Mount(990)));
        fixture.Send("hot", Unsigned(1, 2), "C→S");
        fixture.Send("hrf", Snapshot(Mount(1_000))); // Update in flight for enclosure 1.
        fixture.Send("hpa", Selected());

        Assert.Equal(2, fixture.View.CurrentEnclosure);
        Assert.Equal(990, Assert.Single(fixture.View.Enclosures.Single(enclosure => enclosure.Enclosure == 1).Reading.Mounts).Serenity);
        Assert.Empty(fixture.View.Enclosures.Single(enclosure => enclosure.Enclosure == 2).Reading.Mounts);
        fixture.Send("hrf", Snapshot(Mount(1_010)));
        Assert.Empty(fixture.View.Enclosures.Single(enclosure => enclosure.Enclosure == 1).Reading.Mounts);
        Assert.Equal(1_010, Assert.Single(fixture.View.Enclosures.Single(enclosure => enclosure.Enclosure == 2).Reading.Mounts).Serenity);
    }

    [Fact]
    public void ConcurrentDifferentSelectionsDoNotAssignUnnumberedResponses()
    {
        var fixture = new LiveFixture();
        fixture.Send("hot", Unsigned(1, 1), "C→S");
        fixture.Send("hot", Unsigned(1, 2), "C→S");
        fixture.Send("hpa", Selected(Mount(990)));
        fixture.Send("hpa", Selected());

        Assert.Null(fixture.View.CurrentEnclosure);
        Assert.Empty(fixture.View.Enclosures);
    }

    private sealed class LiveFixture
    {
        private readonly DofusClientWindowKey _window = new(101, 1201);
        private readonly LiveBreedingService _service;
        private readonly ProtocolMap _map = ProtocolMap.Load(
            Path.Combine(AppContext.BaseDirectory, "NetworkFixtures", "protocol-map.json"));
        private DateTime _time = new(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);

        internal LiveFixture()
        {
            var tuple = new TcpSocketTuple(IPAddress.Parse("192.0.2.10"), 48001, IPAddress.Parse("198.51.100.20"), 5555);
            var bindings = new LiveDofusClientNetworkBindings();
            Assert.True(bindings.Observe(tuple, 1, [new(tuple, 1201)], [_window]));
            Assert.Equal(_window, bindings.ResolveConnection(tuple, 1, 2));
            _service = new(bindings);
        }

        internal LiveBreedingView View => _service.ViewForWindow(_window)!;

        internal void Send(string key, byte[] body, string direction = "S→C") =>
            _service.Observe(_map, new(direction, key, body, _time = _time.AddSeconds(1),
                null, 1, ConnectionId: 2, CandidateWindow: _window));
    }

    private static byte[] Selected(params byte[][] mounts) => Bytes(1, Bytes(2, Enclosure(mounts)));

    private static byte[] Snapshot(params byte[][] mounts) => Bytes(1, Enclosure(mounts));

    private static byte[] Enclosure(params byte[][] mounts) => Message(
        Bytes(1, Packed(0, 3)),
        Gauge(4, 80_000), Gauge(5, 0), Gauge(0, 67_340),
        Gauge(2, 72_940), Gauge(1, 60_010), Gauge(3, 69_980),
        Message(mounts));

    private static byte[] Gauge(ulong gauge, ulong fuel) =>
        Bytes(4, Message(fuel == 0 ? [] : Unsigned(2, fuel), gauge == 0 ? [] : Unsigned(3, gauge)));

    private static byte[] Mount(long serenity, int wireField = 5) => Bytes(wireField, Message(
        Id(),
        Bytes(2, Message(Unsigned(5, unchecked((ulong)serenity)),
            Bytes(6, Message(Unsigned(1, 1), Unsigned(2, 8_020))),
            Unsigned(8, 96), Unsigned(13, 160_000)))));

    private static byte[] Transfer(ulong status) => Bytes(1, Message(Id(), Unsigned(2, status)));

    private static byte[] Id() => Bytes(1, Encoding.UTF8.GetBytes(MountId.ToString("D")));
}
