using BestCrush.Network.Protocol;

namespace BestCrush.Services;

internal sealed record ObservedEquipmentAcquisition(long ItemId, ulong Uid, EquipmentAcquisition Acquisition);

/// <summary>Associates confirmed acquisitions within one verified game connection.</summary>
internal sealed class EquipmentAcquisitionTracker
{
    private static readonly TimeSpan PurchaseWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OfferWindow = TimeSpan.FromMinutes(5);
    private readonly Dictionary<ulong, (ulong ItemId, ulong Price)> _offers = [];
    private (DateTime At, NetworkCaptureLease Lease)? _offerContext;
    private Purchase? _pending;

    private sealed class Purchase(PurchaseRequestObservation request, DateTime at, NetworkCaptureLease lease)
    {
        public PurchaseRequestObservation Request { get; } = request;
        public DateTime At { get; } = at;
        public NetworkCaptureLease Lease { get; } = lease;
        public ulong? ItemId { get; set; }
        public bool Confirmed { get; set; }
        public HashSet<(ulong ItemId, ulong Uid)> Added { get; } = [];
    }

    internal void RememberOffers(MarketObservation market, DofusWireMessage message, NetworkCaptureLease lease)
    {
        if (message.Direction != "S→C" || message.CandidateWindow is null) return;
        _offers.Clear();
        _offerContext = (message.ObservedAtUtc, lease);
        foreach (var offer in market.Offers)
            if (offer.Ladder is [> 0] && _offers.Count < 4096)
                _offers[offer.OfferId] = (market.ItemId, offer.Ladder[0]);
    }

    internal ObservedEquipmentAcquisition? Observe(ProtocolMap map, DofusWireMessage message, byte[] body, NetworkCaptureLease lease)
    {
        if (message.CandidateWindow is null) return null;
        var at = message.ObservedAtUtc;
        if (message.Direction == "C→S" && (message.Key == map.PurchaseRequest || message.Key == map.CurrentPurchaseRequest))
        {
            _pending = null;
            var decoded = message.Key == map.CurrentPurchaseRequest
                ? CurrentPurchaseDecoder.DecodeRequest(body) : SemanticDecoders.TryDecodePurchaseRequest(body);
            if (decoded is { Quantity: 1, Price: > 0 and <= long.MaxValue } request)
            {
                _pending = new(request, at, lease);
                if (_offerContext is { } context && context.Lease == lease && at >= context.At && at - context.At <= OfferWindow &&
                    _offers.TryGetValue(request.OfferId, out var known) && known.Price == request.Price)
                    _pending.ItemId = known.ItemId;
            }
            return null;
        }

        if (message.Direction != "S→C") return null;
        if (message.Key == map.CraftOutput &&
            SemanticDecoders.TryDecodeCraftOutput(body) is { Quantity: 1, ItemId: > 0 and <= long.MaxValue, ItemUid: > 0 } output)
            return new((long)output.ItemId, output.ItemUid, new(EquipmentAcquisitionKind.Craft, null, at));

        if (_pending is not { } purchase) return null;
        if (lease != purchase.Lease || at < purchase.At || at - purchase.At > PurchaseWindow)
        {
            _pending = null;
            return null;
        }

        var decodedOffer = message.Key switch
        {
            var key when key == map.CurrentPurchaseOffer => CurrentPurchaseDecoder.DecodeOffer(body),
            var key when key == map.PurchaseOffer => SemanticDecoders.TryDecodePurchaseOffer(body),
            _ => null
        };
        var decodedReceipt = message.Key switch
        {
            var key when key == map.CurrentPurchaseReceipt => CurrentPurchaseDecoder.DecodeReceipt(body),
            var key when key == map.PurchaseReceipt => SemanticDecoders.TryDecodePurchaseReceipt(body),
            _ => null
        };

        if (decodedOffer is { } offer && offer.OfferId == purchase.Request.OfferId)
        {
            if (purchase.ItemId is { } expected && expected != offer.ItemId)
            {
                _pending = null;
                return null;
            }
            purchase.ItemId = offer.ItemId;
        }
        else if (decodedReceipt is { Quantity: 1 } receipt && receipt.OfferId == purchase.Request.OfferId)
            purchase.Confirmed = true;
        else if (message.Key == "iqx" && LiveStockProtocol.Decode("iqx", message.Body) is { } stock)
        {
            foreach (var item in stock.Items.Where(item => item.Quantity == 1))
                purchase.Added.Add((item.ItemId, item.Uid));
        }
        else if (message.Key == map.InventoryAdd && SemanticDecoders.TryDecodeInventoryAdd(body) is { Quantity: 1 } item)
            purchase.Added.Add((item.ItemId, item.ItemUid));

        if (purchase.Added.Count > 64)
        {
            _pending = null;
            return null;
        }
        if (!purchase.Confirmed || purchase.ItemId is not (> 0 and <= long.MaxValue)) return null;
        var candidates = purchase.Added.Where(item => item.ItemId == purchase.ItemId && item.Uid > 0).Take(2).ToArray();
        if (candidates.Length == 0) return null;
        _pending = null;
        return candidates.Length == 1 ? new((long)candidates[0].ItemId, candidates[0].Uid,
            new(EquipmentAcquisitionKind.Purchase, (long)purchase.Request.Price, at)) : null;
    }
}
