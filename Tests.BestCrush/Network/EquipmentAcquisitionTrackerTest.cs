using BestCrush.Network.Protocol;
using BestCrush.Services;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class EquipmentAcquisitionTrackerTest
{
    private static readonly DateTime At = new(2026, 11, 15, 16, 0, 0, DateTimeKind.Utc);
    private static readonly NetworkCaptureLease Lease = new("Kourial", 1);
    private static readonly DofusClientWindowKey Window = new(111, 222);
    private static readonly ProtocolMap Map = ProtocolMap.Load("missing-fixture-map.json");

    private static ObservedEquipmentAcquisition? Observe(EquipmentAcquisitionTracker tracker, string code, byte[] body,
        int seconds, string direction = "S→C") =>
        tracker.Observe(Map, new(direction, code, body, At.AddSeconds(seconds), Lease, 1,
            ConnectionId: 1, CandidateWindow: Window), body, Lease);

    private static byte[] Request() => Message(Unsigned(1, 1234), Unsigned(2, 1), Unsigned(5, 501));
    private static byte[] Receipt(ulong offer = 501) => Message(Unsigned(2, 1), Unsigned(3, offer));
    private static byte[] Inventory(params ulong[] uids) => Bytes(2, Message(uids.Select(uid =>
        Bytes(1, Message(Unsigned(3, 1), Unsigned(4, 42), Unsigned(5, uid)))).ToArray()));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PairsMarketOfferAndConfirmedPaymentWithTheReceivedInstance(bool receiptFirst)
    {
        var tracker = new EquipmentAcquisitionTracker();
        byte[] body = [];
        tracker.RememberOffers(new(42, [new(501, 42, [1234])]),
            new("S→C", "jzs", body, At, Lease, 1, ConnectionId: 1, CandidateWindow: Window), Lease);
        Assert.Null(Observe(tracker, "kei", Request(), 1, "C→S"));
        Assert.Null(receiptFirst ? Observe(tracker, "kbd", Receipt(), 2) : Observe(tracker, "iqx", Inventory(88), 2));
        var result = Assert.IsType<ObservedEquipmentAcquisition>(receiptFirst
            ? Observe(tracker, "iqx", Inventory(88), 3) : Observe(tracker, "kbd", Receipt(), 3));
        Assert.Equal(42, result.ItemId);
        Assert.Equal(88UL, result.Uid);
        Assert.Equal(EquipmentAcquisitionKind.Purchase, result.Acquisition.Kind);
        Assert.Equal(1234, result.Acquisition.PricePaid);
        Assert.Null(Observe(tracker, "kbd", Receipt(), 4));
    }

    [Fact]
    public void CurrentPurchaseKeepsTheConfirmedPriceAndTheInventoryUid()
    {
        var tracker = new EquipmentAcquisitionTracker();
        // Current game sends request, item/offer, inventory upsert and receipt in one burst.
        Assert.Null(Observe(tracker, "kbf", Message(Unsigned(1, 1), Unsigned(3, 501), Unsigned(5, 10)), 0, "C→S"));
        Assert.Null(Observe(tracker, "kas", Message(Unsigned(2, 42), Unsigned(3, 9), Unsigned(4, 501)), 0));
        Assert.Null(Observe(tracker, "iqx", Inventory(88), 0));
        Assert.Null(Observe(tracker, "jzx", Message(Unsigned(2, 501), Unsigned(3, 10)), 0));
        var result = Assert.IsType<ObservedEquipmentAcquisition>(
            Observe(tracker, "jzx", Message(Unsigned(2, 501), Unsigned(3, 1)), 0));
        Assert.Equal(42, result.ItemId);
        Assert.Equal(88UL, result.Uid);
        Assert.Equal(10, result.Acquisition.PricePaid);
    }

    [Fact]
    public void AnAmbiguousOrUnconfirmedInventoryAdditionCannotSetAnAcquisition()
    {
        var tracker = new EquipmentAcquisitionTracker();
        Assert.Null(Observe(tracker, "kei", Request(), 0, "C→S"));
        Assert.Null(Observe(tracker, "kef", Message(Unsigned(1, 42), Unsigned(2, 501)), 1));
        Assert.Null(Observe(tracker, "iqx", Inventory(88, 89), 2));
        Assert.Null(Observe(tracker, "kbd", Receipt(500), 3));
        Assert.Null(Observe(tracker, "kbd", Receipt(), 4));
    }

    [Fact]
    public void CraftOutputUsesTheReturnedItemUid()
    {
        var tracker = new EquipmentAcquisitionTracker();
        var body = Bytes(1, Bytes(5, Message(Unsigned(1, 99), Unsigned(2, 1), Unsigned(5, 42))));
        Assert.Null(Observe(tracker, "iuq", body, 0, "C→S"));
        var result = Assert.IsType<ObservedEquipmentAcquisition>(Observe(tracker, "iuq", body, 1));
        Assert.Equal(99UL, result.Uid);
        Assert.Equal(EquipmentAcquisitionKind.Craft, result.Acquisition.Kind);
        Assert.Null(result.Acquisition.PricePaid);
    }
}
