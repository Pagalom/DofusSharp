using BestCrush.Network.Protocol;

namespace BestCrush.Services;

/// <summary>
/// Ordered semantic routing for decoded Dofus network messages.
/// Capture/framing remain in DofusNetworkCaptureService.
/// </summary>
internal sealed class DofusNetworkMessageProcessor(
    NetworkObservationWriter observationWriter,
    NetworkDebugWriter debugWriter)
{
    private readonly Dictionary<
        ulong,
        ItemDetailObservation> _itemDetails = [];

    private PurchaseRequestObservation?
        _pendingPurchaseRequest;

    private DateTime
        _pendingPurchaseRequestAtUtc;

    internal async Task ProcessMessageAsync(
        ProtocolMap? map,
        DofusWireMessage message,
        CancellationToken cancellationToken)
    {
        await debugWriter.WriteWireDebugAsync(
            message,
            cancellationToken);

        if (map is null)
            return;

        if (map.ItemDetail is not null &&
            string.Equals(
                message.Key,
                map.ItemDetail,
                StringComparison.Ordinal))
        {
            ItemDetailObservation? item =
                SemanticDecoders.TryDecodeItemDetail(
                    message.Body);

            if (item is not null)
            {
                _itemDetails[item.ItemUid] = item;
                await observationWriter.RememberLastEquipmentAsync(
                    item.ItemId,
                    message.ObservedAtUtc,
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
                    message.Body);

            if (item is not null)
            {
                _itemDetails[item.ItemUid] = item;
                await observationWriter.RememberLastEquipmentAsync(
                    item.ItemId,
                    message.ObservedAtUtc,
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
                    message.Body);

            if (item is not null)
            {
                _itemDetails[item.ItemUid] = item;
                await observationWriter.RememberLastEquipmentAsync(
                    item.ItemId,
                    message.ObservedAtUtc,
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
                    message.Body);

            if (result is not null)
            {
                _itemDetails[result.Item.ItemUid] =
                    result.Item;

                await observationWriter.RememberLastEquipmentAsync(
                    result.Item.ItemId,
                    message.ObservedAtUtc,
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
                    message.Body);

            if (purchase is not null)
            {
                _pendingPurchaseRequest =
                    purchase;

                _pendingPurchaseRequestAtUtc =
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
                    message.Body);

            bool matchesRecentPurchase =
                offer is not null &&
                _pendingPurchaseRequest is not null &&
                offer.OfferId ==
                    _pendingPurchaseRequest.OfferId &&
                message.ObservedAtUtc -
                    _pendingPurchaseRequestAtUtc <=
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
                    message.Body);

            if (receipt is not null &&
                _pendingPurchaseRequest is not null &&
                receipt.OfferId ==
                    _pendingPurchaseRequest.OfferId)
            {
                await debugWriter.WriteEventDebugAsync(
                    message.ObservedAtUtc,
                    $"[PURCHASE-CONFIRMED] offer={receipt.OfferId} x{_pendingPurchaseRequest.Quantity} price={_pendingPurchaseRequest.Price} K",
                    cancellationToken);

                _pendingPurchaseRequest = null;
                _pendingPurchaseRequestAtUtc = default;
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
                    message.Body);

            if (listing is not null)
            {
                await observationWriter.RememberLastEquipmentAsync(
                    listing.ItemId,
                    message.ObservedAtUtc,
                    cancellationToken);

                await debugWriter.WriteEventDebugAsync(
                    message.ObservedAtUtc,
                    $"[LISTING] MarketUid={listing.MarketListingUid} ItemId={listing.ItemId} x{listing.Quantity} price={listing.Price} K",
                    cancellationToken);
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
                    message.Body);

            if (market is not null)
            {
                await debugWriter.WriteEventDebugAsync(
                    message.ObservedAtUtc,
                    FormatMarketDebug(market),
                    cancellationToken);

                await observationWriter.PersistMarketAsync(
                    market,
                    message.ObservedAtUtc,
                    cancellationToken);
            }

            return;
        }

        if (map.CrushResult is not null &&
            string.Equals(
                message.Key,
                map.CrushResult,
                StringComparison.Ordinal))
        {
            CrushObservation? crush =
                SemanticDecoders.TryDecodeCrush(
                    message.Body);

            if (crush is not null)
            {
                foreach (CrushLineObservation line in crush.Lines)
                {
                    _itemDetails.TryGetValue(
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
                    _itemDetails,
                    message.ObservedAtUtc,
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
