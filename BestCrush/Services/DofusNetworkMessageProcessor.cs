using BestCrush.Network.Protocol;

namespace BestCrush.Services;

/// <summary>
/// Ordered semantic routing for decoded Dofus network messages.
/// Capture/framing remain in DofusNetworkCaptureService.
/// </summary>
internal sealed class DofusNetworkMessageProcessor(
    NetworkObservationWriter observationWriter,
    NetworkDebugWriter debugWriter,
    CurrentServerState currentServerState)
{
    // Single ordered worker; state is scoped to one opaque, bidirectional
    // TCP connection ID. Never use a selected window or character name as
    // evidence that two messages belong to the same connection.
    private const int MaximumCorrelationFlows = 64;
    // Association lifetime is the capture lease, not the business-cache LRU.
    private const int MaximumVerifiedWindowTokens = 8192;
    private static readonly TimeSpan CorrelationIdleTimeout = TimeSpan.FromHours(1);

    private sealed class FlowCorrelation
    {
        internal readonly RecentItemDetailsCache ItemDetails = new();
        internal PurchaseRequestObservation? PendingPurchaseRequest;
        internal DateTime PendingPurchaseRequestAtUtc;
        internal DateTime LastSeenUtc;
    }

    private readonly Dictionary<long, FlowCorrelation> _correlations = [];
    // Null means permanently quarantined within this lease/epoch.
    // Keep the first verified Windows context independently of short-lived
    // UID/purchase caches, including after business-cache eviction.
    private readonly Dictionary<long, DofusClientWindowKey?>
        _verifiedWindowByConnection = [];
    private bool _windowBindingOverflow;
    private NetworkCaptureLease? _lastLease;
    private long? _lastCaptureEpoch;

    private FlowCorrelation CorrelationFor(long connectionId, DateTime seenAtUtc)
    {
        if (_correlations.TryGetValue(connectionId, out FlowCorrelation? existing))
        {
            existing.LastSeenUtc = seenAtUtc;
            return existing;
        }

        foreach (long stale in _correlations
            .Where(pair => seenAtUtc - pair.Value.LastSeenUtc >= CorrelationIdleTimeout)
            .Select(pair => pair.Key).ToArray())
            _correlations.Remove(stale);

        if (_correlations.Count >= MaximumCorrelationFlows)
            _correlations.Remove(_correlations.MinBy(pair => pair.Value.LastSeenUtc).Key);

        FlowCorrelation created = new() { LastSeenUtc = seenAtUtc };
        _correlations.Add(connectionId, created);
        return created;
    }

    internal async Task ProcessMessageAsync(
        ProtocolMap? map,
        DofusWireMessage message,
        CancellationToken cancellationToken)
    {
        if (_lastLease != message.CaptureLease ||
            _lastCaptureEpoch != message.CaptureEpoch)
        {
            // No UID or purchase state survives server selection / Stop+Start.
            _correlations.Clear();
            _verifiedWindowByConnection.Clear();
            _windowBindingOverflow = false;
            _lastLease = message.CaptureLease;
            _lastCaptureEpoch = message.CaptureEpoch;
        }

        // All live packets must have an explicit per-connection token.
        // Replay tests default to their own synthetic token (1).
        if (message.ConnectionId <= 0 ||
            message.CaptureLease is not { } lease ||
            !currentServerState.IsCaptureLeaseActive(lease))
            return;

        if (_windowBindingOverflow)
            return;

        // A verified OS window is NOT a game identity. It is nevertheless a
        // stable expectation for one opaque logical TCP token. A conflicting
        // assignment must not allow caches to cross clients, even if the
        // business-cache entry was evicted long ago.
        if (_verifiedWindowByConnection.TryGetValue(
                message.ConnectionId, out DofusClientWindowKey? firstWindow))
        {
            if (firstWindow is null) // already quarantined
                return;

            if (message.CandidateWindow is { } changedWindow &&
                firstWindow.Value != changedWindow)
            {
                _verifiedWindowByConnection[message.ConnectionId] = null;
                _correlations.Remove(message.ConnectionId);
                return;
            }
        }
        else if (message.CandidateWindow is { } firstVerifiedWindow)
        {
            if (_verifiedWindowByConnection.Count >=
                MaximumVerifiedWindowTokens)
            {
                // Do not silently drop an earlier association and then
                // accept a recycled logical token for a different window.
                _windowBindingOverflow = true;
                _correlations.Clear();
                return;
            }

            _verifiedWindowByConnection.Add(
                message.ConnectionId, firstVerifiedWindow);
        }

        // Absence of window metadata is not proof of a different owner:
        // the existing manually chosen server remains authoritative.
        FlowCorrelation correlation =
            CorrelationFor(message.ConnectionId, message.ObservedAtUtc);
        RecentItemDetailsCache itemDetails = correlation.ItemDetails;

        await debugWriter.WriteWireDebugAsync(
            message,
            cancellationToken);

        if (map is null ||
            !currentServerState.IsCaptureLeaseActive(lease))
            return;

        // Translate active wire fields to the canonical decoder schema.
        // Wire debug above intentionally retains original captured bytes.
        byte[] body = map.NormalizeBody(message.Key, message.Body);

        if ((map.ItemDetail is not null &&
             string.Equals(
                 message.Key,
                 map.ItemDetail,
                 StringComparison.Ordinal)) ||
            (map.CurrentItemDetail is not null &&
             string.Equals(
                 message.Key,
                 map.CurrentItemDetail,
                 StringComparison.Ordinal)))
        {
            ItemDetailObservation? item =
                SemanticDecoders.TryDecodeItemDetail(
                    body);

            if (item is not null)
            {
                itemDetails.Remember(item);
                await observationWriter.RememberLastEquipmentAsync(
                    item.ItemId,
                    message.ObservedAtUtc,
                    lease,
                    cancellationToken);

                await debugWriter.WriteEventDebugAsync(
                    message.ObservedAtUtc,
                    $"[ITEM] UID={item.ItemUid} ItemId={item.ItemId} x{item.Quantity} | {FormatStats(item.Stats)}",
                    cancellationToken);
            }

            return;
        }

        if (map.InventoryAdd is not null &&
            string.Equals(
                message.Key,
                map.InventoryAdd,
                StringComparison.Ordinal))
        {
            ItemDetailObservation? item =
                SemanticDecoders.TryDecodeInventoryAdd(
                    body);

            if (item is not null)
            {
                itemDetails.Remember(item);
                await observationWriter.RememberLastEquipmentAsync(
                    item.ItemId,
                    message.ObservedAtUtc,
                    lease,
                    cancellationToken);

                await debugWriter.WriteEventDebugAsync(
                    message.ObservedAtUtc,
                    $"[INVENTORY-ADD] UID={item.ItemUid} ItemId={item.ItemId} x{item.Quantity} | {FormatStats(item.Stats)}",
                    cancellationToken);
            }

            return;
        }

        if (map.CraftOutput is not null &&
            string.Equals(
                message.Key,
                map.CraftOutput,
                StringComparison.Ordinal))
        {
            ItemDetailObservation? item =
                SemanticDecoders.TryDecodeCraftOutput(
                    body);

            if (item is not null)
            {
                itemDetails.Remember(item);
                await observationWriter.RememberLastEquipmentAsync(
                    item.ItemId,
                    message.ObservedAtUtc,
                    lease,
                    cancellationToken);

                await debugWriter.WriteEventDebugAsync(
                    message.ObservedAtUtc,
                    $"[CRAFT-OUTPUT] UID={item.ItemUid} ItemId={item.ItemId} x{item.Quantity} | {FormatStats(item.Stats)}",
                    cancellationToken);
            }

            return;
        }

        if (map.SmithmagicResult is not null &&
            string.Equals(
                message.Key,
                map.SmithmagicResult,
                StringComparison.Ordinal))
        {
            SmithmagicResultObservation? result =
                SemanticDecoders.TryDecodeSmithmagicResult(
                    body);

            if (result is not null)
            {
                itemDetails.Remember(result.Item);

                await observationWriter.RememberLastEquipmentAsync(
                    result.Item.ItemId,
                    message.ObservedAtUtc,
                    lease,
                    cancellationToken);

                await debugWriter.WriteEventDebugAsync(
                    message.ObservedAtUtc,
                    $"[WORKSHOP-RESULT] code={result.ResultCode} UID={result.Item.ItemUid} ItemId={result.Item.ItemId} | {FormatStats(result.Item.Stats)}",
                    cancellationToken);
            }

            return;
        }

        if (map.PurchaseRequest is not null &&
            string.Equals(
                message.Key,
                map.PurchaseRequest,
                StringComparison.Ordinal))
        {
            PurchaseRequestObservation? purchase =
                SemanticDecoders.TryDecodePurchaseRequest(
                    body);

            if (purchase is not null)
            {
                correlation.PendingPurchaseRequest =
                    purchase;

                correlation.PendingPurchaseRequestAtUtc =
                    message.ObservedAtUtc;

                await debugWriter.WriteEventDebugAsync(
                    message.ObservedAtUtc,
                    $"[PURCHASE-REQUEST] offer={purchase.OfferId} x{purchase.Quantity} price={purchase.Price} K",
                    cancellationToken);
            }

            return;
        }

        if (map.PurchaseOffer is not null &&
            string.Equals(
                message.Key,
                map.PurchaseOffer,
                StringComparison.Ordinal))
        {
            PurchaseOfferObservation? offer =
                SemanticDecoders.TryDecodePurchaseOffer(
                    body);

            bool matchesRecentPurchase =
                offer is not null &&
                correlation.PendingPurchaseRequest is not null &&
                offer.OfferId ==
                    correlation.PendingPurchaseRequest.OfferId &&
                message.ObservedAtUtc -
                    correlation.PendingPurchaseRequestAtUtc <=
                        TimeSpan.FromSeconds(5);

            if (offer is not null &&
                matchesRecentPurchase)
            {
                await debugWriter.WriteEventDebugAsync(
                    message.ObservedAtUtc,
                    $"[MARKET-REFRESH] source=kef ItemId={offer.ItemId} ladder=[{string.Join(", ", offer.Ladder)}]",
                    cancellationToken);

                if (offer.Ladder.Count > 0)
                {
                    MarketObservation refreshedMarket =
                        new(
                            offer.ItemId,
                            [
                                new MarketOffer(
                                    offer.OfferId,
                                    offer.ItemId,
                                    offer.Ladder)
                            ]
                        );

                    // For stackable objects (resources/runes), kef is the
                    // exact refreshed x1/x10/x100/x1000 ladder shown by Dofus
                    // immediately after the purchase. Equipment offers carry
                    // per-instance information, so jzn remains authoritative
                    // for their market-wide minimum.
                    await observationWriter.PersistMarketAsync(
                        refreshedMarket,
                        message.ObservedAtUtc,
                        lease,
                        cancellationToken,
                        allowEquipmentPriceRefresh: false);
                }
            }

            return;
        }

        if (map.PurchaseReceipt is not null &&
            string.Equals(
                message.Key,
                map.PurchaseReceipt,
                StringComparison.Ordinal))
        {
            PurchaseReceiptObservation? receipt =
                SemanticDecoders.TryDecodePurchaseReceipt(
                    body);

            if (receipt is not null &&
                correlation.PendingPurchaseRequest is not null &&
                receipt.OfferId ==
                    correlation.PendingPurchaseRequest.OfferId)
            {
                await debugWriter.WriteEventDebugAsync(
                    message.ObservedAtUtc,
                    $"[PURCHASE-CONFIRMED] offer={receipt.OfferId} x{correlation.PendingPurchaseRequest.Quantity} price={correlation.PendingPurchaseRequest.Price} K",
                    cancellationToken);

                correlation.PendingPurchaseRequest = null;
                correlation.PendingPurchaseRequestAtUtc = default;
            }

            return;
        }

        if (map.MarketListingCreated is not null &&
            string.Equals(
                message.Key,
                map.MarketListingCreated,
                StringComparison.Ordinal))
        {
            MarketListingCreatedObservation? listing =
                SemanticDecoders.TryDecodeMarketListingCreated(
                    body);

            if (listing is not null)
            {
                await observationWriter.RememberLastEquipmentAsync(
                    listing.ItemId,
                    message.ObservedAtUtc,
                    lease,
                    cancellationToken);

                await debugWriter.WriteEventDebugAsync(
                    message.ObservedAtUtc,
                    $"[LISTING] MarketUid={listing.MarketListingUid} ItemId={listing.ItemId} x{listing.Quantity} price={listing.Price} K",
                    cancellationToken);
            }

            return;
        }

        if (map.MarketSelectionResponse is not null &&
            string.Equals(
                message.Key,
                map.MarketSelectionResponse,
                StringComparison.Ordinal))
        {
            ulong? itemId =
                SemanticDecoders.TryDecodeMarketSelectionItemId(
                    body);

            if (itemId is not null)
            {
                // A selection is not a price observation. The writer
                // resolves the ID against the equipment table and checks
                // that the capture lease is still authoritative.
                await observationWriter.RememberLastEquipmentAsync(
                    itemId.Value,
                    message.ObservedAtUtc,
                    lease,
                    cancellationToken);

                await debugWriter.WriteEventDebugAsync(
                    message.ObservedAtUtc,
                    $"[MARKET-SELECTION] ItemId={itemId.Value}",
                    cancellationToken);

                // The current jzs offer list differs from legacy jzn.
                // Keep separate validation for equipment x1 and for the
                // independently verified four-lot rune/resource prices.
                // Invalid or empty rows never flow into SQLite.
                MarketObservation? prices =
                    SemanticDecoders.TryDecodeJzsEquipmentPrices(body);

                if (prices is not null &&
                    prices.ItemId == itemId.Value)
                {
                    await debugWriter.WriteEventDebugAsync(
                        message.ObservedAtUtc,
                        $"[MARKET-JZS-VERIFIED-X1] {FormatMarketDebug(prices)}",
                        cancellationToken);

                    await observationWriter.PersistMarketAsync(
                        prices,
                        message.ObservedAtUtc,
                        lease,
                        cancellationToken,
                        requireEquipment: true);
                }

                MarketObservation? stackablePrices =
                    SemanticDecoders.TryDecodeJzsStackablePrices(body);

                if (stackablePrices is not null &&
                    stackablePrices.ItemId == itemId.Value)
                {
                    // No category inference from the wire group number:
                    // the writer verifies that the ID belongs to a
                    // catalogue rune or resource, never equipment.
                    await observationWriter.PersistMarketAsync(
                        stackablePrices,
                        message.ObservedAtUtc,
                        lease,
                        cancellationToken,
                        requireStackable: true);
                }
            }

            return;
        }

        if (map.PriceList is not null &&
            string.Equals(
                message.Key,
                map.PriceList,
                StringComparison.Ordinal))
        {
            MarketObservation? market =
                SemanticDecoders.TryDecodeMarket(
                    body);

            if (market is not null)
            {
                await debugWriter.WriteEventDebugAsync(
                    message.ObservedAtUtc,
                    FormatMarketDebug(market),
                    cancellationToken);

                await observationWriter.PersistMarketAsync(
                    market,
                    message.ObservedAtUtc,
                    lease,
                    cancellationToken);
            }

            return;
        }

        if ((map.CrushResult is not null &&
             string.Equals(
                 message.Key,
                 map.CrushResult,
                 StringComparison.Ordinal)) ||
            (map.CurrentCrushResult is not null &&
             string.Equals(
                 message.Key,
                 map.CurrentCrushResult,
                 StringComparison.Ordinal)))
        {
            CrushObservation? crush =
                SemanticDecoders.TryDecodeCrush(
                    body);

            if (crush is not null)
            {
                foreach (CrushLineObservation line in crush.Lines)
                {
                    itemDetails.TryGetValue(
                        line.ItemUid,
                        out ItemDetailObservation? knownItem);

                    await debugWriter.WriteEventDebugAsync(
                        message.ObservedAtUtc,
                        FormatCrushDebug(
                            line,
                            knownItem),
                        cancellationToken);
                }

                await observationWriter.PersistCrushAsync(
                    crush,
                    itemDetails.Items,
                    message.ObservedAtUtc,
                    lease,
                    cancellationToken);
            }
        }
    }


    private static string FormatStats(
        IReadOnlyList<ItemStatObservation> stats)
    {
        return stats.Count == 0
            ? "stats=(none)"
            : "stats=" +
              string.Join(
                  ", ",
                  stats.Select(
                      stat =>
                          $"{stat.EffectId}={stat.Value}"));
    }

    private static string FormatMarketDebug(
        MarketObservation market)
    {
        int[] quantities =
            [1, 10, 100, 1000];

        List<string> prices = [];

        for (
            int index = 0;
            index < quantities.Length;
            index++)
        {
            ulong[] candidates =
                market.Offers
                    .Where(
                        offer =>
                            offer.Ladder.Count > index &&
                            offer.Ladder[index] > 0)
                    .Select(
                        offer =>
                            offer.Ladder[index])
                    .ToArray();

            if (candidates.Length == 0)
            {
                continue;
            }

            prices.Add(
                $"x{quantities[index]}={candidates.Min()} K");
        }

        return
            $"[MARKET] ItemId={market.ItemId} offers={market.Offers.Count}" +
            (
                prices.Count == 0
                    ? " | no-price"
                    : " | " +
                      string.Join(
                          " | ",
                          prices)
            );
    }

    private static string FormatCrushDebug(
        CrushLineObservation line,
        ItemDetailObservation? item)
    {
        string runes =
            line.Runes.Count == 0
                ? "(none)"
                : string.Join(
                    ", ",
                    line.Runes.Select(
                        rune =>
                            $"{rune.RuneItemId}x{rune.Quantity}"));

        return
            $"[CRUSH] UID={line.ItemUid} " +
            $"ItemId={(item?.ItemId.ToString() ?? "?")} " +
            $"coefficient={line.CoefficientPercent:0.#####}% " +
            $"runes={runes}";
    }


}
