using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;
using DofusSharp.DofusDb.ApiClients;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Reflection;

namespace Tests.BestCrush;

public sealed class LiveForgemagingTest
{
    [Fact]
    public void WorkshopDiagnosticsExcludeAuthenticationChatAndUnrelatedInventorySnapshots()
    {
        Assert.True(ForgemagingWireDiagnostics.IsWorkshopMessage("kco"));
        Assert.True(ForgemagingWireDiagnostics.IsWorkshopMessage("jxu"));
        Assert.False(ForgemagingWireDiagnostics.IsWorkshopMessage("irl"));
        Assert.False(ForgemagingWireDiagnostics.IsWorkshopMessage("jns"));
        Assert.False(ForgemagingWireDiagnostics.IsWorkshopMessage("authentication"));
    }
    [Fact]
    public void CaptureRequiresCurrentWindowAndRevocableServerLease()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var clients = new CurrentDofusClientsState();
        DofusClientWindowKey one = new(111, 1), two = new(222, 2);
        clients.Update([
            new DofusWindowInfo(new IntPtr(111), "One - Eniripsa - 3.7.13.17 - Release", "Dofus", 0, 0, 100, 100) { ProcessId = 1 },
            new DofusWindowInfo(new IntPtr(222), "Two - Eniripsa - 3.7.13.17 - Release", "Dofus", 0, 0, 100, 100) { ProcessId = 2 }], one);
        var servers = new CurrentServerState(); servers.SelectServer("Tylezia");
        var catalog = new ActivityCatalogService(Mock.Of<IDofusDbClientsFactory>(), Mock.Of<IHttpClientFactory>(), Path.GetTempPath());
        using var service = new LiveForgemagingService(services.GetRequiredService<IServiceScopeFactory>(), clients, servers,
            new LiveDofusClientNetworkBindings(), catalog, new ForgemagingSessionStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")),
            NullLogger<LiveForgemagingService>.Instance);
        service.Start("Tylezia");
        var message = new DofusWireMessage("S→C", "kch", [], DateTime.UtcNow, servers.GetCaptureLease(), 1,
            ConnectionId: 1, CandidateWindow: one, SyntheticWindowForReplay: true);
        bool Valid(DofusWireMessage m) => (bool)typeof(LiveForgemagingService).GetMethod("Valid", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, [m])!;
        Assert.True(Valid(message));
        Assert.False(Valid(message with { CandidateWindow = two }));
        Assert.False(Valid(message with { CaptureLease = null }));
        Assert.False(Valid(message with { ConnectionId = 0 }));
        Assert.False(Valid(message with { SyntheticWindowForReplay = false }));
        servers.SelectServer("Kourial"); Assert.False(Valid(message));
        servers.SelectServer("Tylezia"); Assert.False(Valid(message));
        clients.SelectWindow(two); Assert.False(service.View("Tylezia").Enabled);
    }

    [Fact]
    public void AnIncompleteResultRemovesStaleAdviceInputs()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var catalog = new ActivityCatalogService(Mock.Of<IDofusDbClientsFactory>(), Mock.Of<IHttpClientFactory>(), Path.GetTempPath());
        using var service = new LiveForgemagingService(services.GetRequiredService<IServiceScopeFactory>(), new CurrentDofusClientsState(),
            new CurrentServerState(), new LiveDofusClientNetworkBindings(), catalog,
            new ForgemagingSessionStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")), NullLogger<LiveForgemagingService>.Instance);
        var session = new ForgemagingSession { Level = 200, Sink = 10, Lines = [new() { Characteristic = Characteristic.Strength, Current = 1, Target = 10 }] };
        typeof(LiveForgemagingService).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, session);
        typeof(LiveForgemagingService).GetMethod("InvalidateJet", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, null);
        Assert.Null(session.Sink); Assert.Null(session.Lines[0].Current);
        Assert.Empty(ForgemagingPlanner.Recommend(session, [new(1, "Rune Fo", Characteristic.Strength, 1, 10)]));
    }

    private static byte[] V(int field, ulong value) { List<byte> b = [(byte)(field * 8)]; do { b.Add((byte)((value & 127) | (value >= 128 ? 128UL : 0))); value >>= 7; } while (value > 0); return b.ToArray(); }
    private static byte[] B(int field, byte[] bytes) => [(byte)(field * 8 + 2), (byte)bytes.Length, .. bytes];
    private static byte[] F(int field, float value) => [(byte)(field * 8 + 5), .. BitConverter.GetBytes(value)];
    private static byte[] Item(ulong uid = 12345) => [.. V(3, 1), .. V(4, 8538), .. V(5, uid),
        .. B(7, [.. V(5, 10), .. V(10, 125)]), .. B(7, [.. V(5, 6), .. V(10, 123)])];

    [Fact]
    public void CurrentWorkshopShapeReadsAbsoluteWellAndNumericEffects()
    {
        // Reconstructed from the observed workshop field layout; synthetic instance UID.
        byte[] body = [.. F(2, 5.5f), .. B(3, [.. B(1, Item()), .. V(4, 63)])];
        var decoded = LiveForgemagingProtocol.WorkshopItem(body);
        Assert.NotNull(decoded); Assert.Equal(5.5, decoded.Value.Sink);
        Assert.Equal(8538, decoded.Value.Item.ItemId); Assert.Equal(12345UL, decoded.Value.Item.Uid);
        Assert.Equal(new long?[] { 10, 6 }, decoded.Value.Item.Effects.Select(e => e.Value));
        Assert.Null(LiveForgemagingProtocol.WorkshopItem([.. B(3, [.. B(1, Item()), .. V(4, 63)])]));
        Assert.Null(LiveForgemagingProtocol.WorkshopItem([.. F(2, float.NaN), .. B(3, [.. B(1, Item()), .. V(4, 63)])]));
        Assert.Null(LiveForgemagingProtocol.WorkshopItem([.. F(2, 0), .. B(3, [.. B(1, [.. Item(), .. V(5, 999)]), .. V(4, 63)])]));
        Assert.Null(LiveForgemagingProtocol.WorkshopItem([.. F(2, 0), .. B(3, B(1, Item())), .. V(4, 63)]));
    }

    [Fact]
    public void RecordedCurrentClientWorkshopPacketHasItsJobInsideTheDescription()
    {
        // Actual 3.7.13.17 kch bytes, with only the instance UID anonymized to 123456.
        byte[] packet = Convert.FromBase64String("FQAAAAAaGQoVGAEg2kIowMQHOgQoClB9OgQoBlB7ID8=");
        var observed = LiveForgemagingProtocol.WorkshopItem(packet);
        Assert.NotNull(observed); Assert.Equal(0, observed.Value.Sink);
        Assert.Equal(8538, observed.Value.Item.ItemId); Assert.Equal(123456UL, observed.Value.Item.Uid);
        Assert.Equal(new ulong[] {125,123}, observed.Value.Item.Effects.Select(e=>e.Code));
        Assert.Equal(new long?[] {10,6}, observed.Value.Item.Effects.Select(e=>e.Value));
    }

    [Fact]
    public void ObservationsKeepRealDeltasEvenOutsideGoalsAndNeverInventAnUnknownWell()
    {
        var session = new ForgemagingSession { GameRecordingId = "recording", Level = 1, Lines = [new() {
            Characteristic = Characteristic.Vitality, Current = 10, Minimum = 7, Maximum = 10, Target = 10 }] };
        var rune = new ForgemagingRune(1523, "Rune Vi", Characteristic.Vitality, 5, 245);
        Dictionary<Characteristic,int> before = new() { [Characteristic.Vitality] = 10, [Characteristic.Chance] = 6 };
        Dictionary<Characteristic,int> after = new() { [Characteristic.Vitality] = 15, [Characteristic.Chance] = 5 };
        Assert.True(ForgemagingSessionActions.RecordObserved(session, rune, ForgemagingOutcome.Neutral, before, after, "event1", DateTime.UtcNow, null));
        Assert.Null(session.Sink); Assert.Equal(245, session.KnownSpent);
        var attempt = Assert.Single(session.Attempts); Assert.True(attempt.FromGame);
        Assert.Equal(15, attempt.After.Single(l => l.Characteristic == Characteristic.Vitality).Current);
        Assert.Equal(6, attempt.Before.Single(l => l.Characteristic == Characteristic.Chance).Current);
        Assert.Equal(5, attempt.After.Single(l => l.Characteristic == Characteristic.Chance).Current);
        Assert.NotNull(attempt.PredictedOdds);
        Assert.Throws<ArgumentException>(() => ForgemagingSessionActions.UndoLast(session));
    }

    [Fact]
    public void ReplayIdsAreIdempotentButDistinctIdenticalFailuresStillCount()
    {
        var session = new ForgemagingSession { GameRecordingId = "recording", Level = 200 };
        var rune = new ForgemagingRune(1, "Rune Fo", Characteristic.Strength, 1, 10);
        Dictionary<Characteristic,int> jet = new() { [Characteristic.Strength] = 1 };
        foreach (string id in new[] { "a", "a", "b" }) ForgemagingSessionActions.RecordObserved(session, rune,
            ForgemagingOutcome.Unconfirmed, jet, jet, id, DateTime.UtcNow, null);
        Assert.Equal(2, session.Attempts.Count); Assert.Equal(20, session.KnownSpent);
        Assert.All(session.Attempts, a => Assert.Equal(ForgemagingOutcome.Unconfirmed, a.Outcome));
    }

    [Fact]
    public void GameRecordingsDoNotReplaceManualOrOtherItemInstances()
    {
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var store = new ForgemagingSessionStore(file);
            store.Save("A", new() { ItemId = 1, ItemName = "Manual" });
            store.Save("A", new() { ItemId = 1, ItemName = "Game1", GameRecordingId = "one" });
            store.Save("A", new() { ItemId = 1, ItemName = "Game2", GameRecordingId = "two" });
            Assert.Equal("Manual", store.Get("A")!.ItemName);
            Assert.Contains("Game1", File.ReadAllText(file)); Assert.Contains("Game2", File.ReadAllText(file));
        }
        finally { File.Delete(file); }
    }
}
