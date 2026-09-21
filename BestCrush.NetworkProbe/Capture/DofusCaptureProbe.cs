using BestCrush.NetworkProbe.Protocol;
using PacketDotNet;
using SharpPcap;

namespace BestCrush.NetworkProbe.Capture;

internal sealed class DofusCaptureProbe : IDisposable
{
    private readonly ICaptureDevice _device;
    private readonly int _port;
    private readonly ProtocolMap _map;
    private readonly bool _showAllMessages;
    private readonly Dictionary<FlowDirection, TcpReassembler> _streams = new();
    private readonly Dictionary<ulong, ItemDetailObservation> _itemDetails = new();
    private readonly Dictionary<ulong, long> _workshopQuantities = new();
    private readonly Dictionary<ulong, (MarketListingRequestObservation Request, MarketListingCreatedObservation Created)> _marketListings = new();
    private readonly List<ItemDetailObservation> _craftIngredientSnapshots = new();

    private PurchaseRequestObservation? _pendingPurchaseRequest;
    private PurchaseOfferObservation? _pendingPurchaseOffer;
    private ItemDetailObservation? _pendingPurchasedItem;
    private SmithmagicRequestObservation? _pendingSmithmagicRequest;
    private SmithmagicRequestObservation? _activeBatchSmithmagicRequest;
    private MarketListingRequestObservation? _pendingMarketListing;
    private ulong? _pendingMarketWithdrawalUid;
    private CraftRequestObservation? _activeCraftRequest;
    private bool _collectCraftIngredients;
    private ulong? _lastWorkshopAddedUid;
    private ulong? _activeSmithmagicTargetUid;

    public DofusCaptureProbe(ICaptureDevice device, int port, ProtocolMap map, bool showAllMessages)
    {
        _device = device;
        _port = port;
        _map = map;
        _showAllMessages = showAllMessages;
    }

    public void Start()
    {
        _device.OnPacketArrival += OnPacketArrival;
        _device.Open(DeviceModes.Promiscuous, 1000);
        _device.Filter = $"tcp port {_port}";
        _device.StartCapture();
    }

    public void Stop()
    {
        try
        {
            _device.StopCapture();
        }
        catch
        {
            // Prototype : l'arrêt doit rester best-effort.
        }
    }

    private void OnPacketArrival(object sender, PacketCapture capture)
    {
        try
        {
            RawCapture raw = capture.GetPacket();
            Packet packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
            IPPacket? ip = packet.Extract<IPPacket>();
            TcpPacket? tcp = packet.Extract<TcpPacket>();

            if (ip is null || tcp is null || tcp.PayloadData.Length == 0)
                return;

            if (tcp.SourcePort != _port && tcp.DestinationPort != _port)
                return;

            FlowDirection direction = tcp.SourcePort == _port
                ? FlowDirection.ServerToClient
                : FlowDirection.ClientToServer;

            TcpReassembler stream = GetStream(direction);
            foreach (ReadOnlyMemory<byte> contiguous in stream.Push(tcp.SequenceNumber, tcp.PayloadData))
            {
                stream.FrameBuffer.Append(contiguous.Span);
                while (stream.FrameBuffer.TryReadFrame(out byte[] frame))
                    HandleFrame(direction, frame);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[capture] {ex.GetType().Name}: {ex.Message}");
        }
    }

    private TcpReassembler GetStream(FlowDirection direction)
    {
        if (_streams.TryGetValue(direction, out TcpReassembler? existing))
            return existing;

        TcpReassembler created = new();
        _streams[direction] = created;
        return created;
    }

    private void HandleFrame(FlowDirection direction, byte[] frame)
    {
        AnkamaAny? any = AnkamaFrameDecoder.FindAny(frame);
        if (any is null)
            return;

        string key = any.Key;
        string arrow = direction == FlowDirection.ServerToClient ? "S→C" : "C→S";

        if (_showAllMessages)
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {arrow} {key} {any.Body.Length} octets");

        if (_map.PriceList is not null &&
            string.Equals(key, _map.PriceList, StringComparison.Ordinal))
        {
            MarketObservation? market = SemanticDecoders.TryDecodeMarket(any.Body);
            if (market is not null)
                ConsoleRenderer.WriteMarket(market);
            else
            {
                Console.WriteLine($"[MARKET?] {key} reçu mais structure non reconnue ({any.Body.Length} octets).");
                ConsoleRenderer.WriteProtoDebug("MARKET", any.Body);
            }
        }


        if (_map.ItemDetail is not null &&
            string.Equals(key, _map.ItemDetail, StringComparison.Ordinal))
        {
            ItemDetailObservation? item = SemanticDecoders.TryDecodeItemDetail(any.Body);
            if (item is not null)
            {
                _itemDetails[item.ItemUid] = item;

                if (_collectCraftIngredients)
                {
                    int existingIndex = _craftIngredientSnapshots.FindIndex(x => x.ItemUid == item.ItemUid);
                    if (existingIndex >= 0)
                        _craftIngredientSnapshots[existingIndex] = item;
                    else
                        _craftIngredientSnapshots.Add(item);
                }

                ConsoleRenderer.WriteItemDetail(item);
            }
            else
            {
                Console.WriteLine($"[ITEM?] {key} reçu mais structure non reconnue ({any.Body.Length} octets).");
                ConsoleRenderer.WriteProtoDebug("ITEM_DETAIL", any.Body);
            }
        }

        if (_map.WorkshopSlotPut is not null &&
            string.Equals(key, _map.WorkshopSlotPut, StringComparison.Ordinal))
        {
            WorkshopSlotObservation? slot = SemanticDecoders.TryDecodeWorkshopSlot(any.Body);
            if (slot is not null)
            {
                if (slot.Delta < 0 && _marketListings.ContainsKey(slot.ItemUid))
                {
                    _pendingMarketWithdrawalUid = slot.ItemUid;
                    Console.WriteLine($"[LISTING-REMOVE] marketListingUid={slot.ItemUid}");
                }
                else
                {
                    ConsoleRenderer.WriteWorkshopSlot(slot);

                    _workshopQuantities.TryGetValue(slot.ItemUid, out long current);
                    long next = current + slot.Delta;

                    if (next <= 0)
                        _workshopQuantities.Remove(slot.ItemUid);
                    else
                        _workshopQuantities[slot.ItemUid] = next;

                    if (slot.Delta > 0)
                        _lastWorkshopAddedUid = slot.ItemUid;
                }
            }
            else
            {
                Console.WriteLine($"[WORKSHOP?] {key} reçu mais structure non reconnue ({any.Body.Length} octets).");
                ConsoleRenderer.WriteProtoDebug("WORKSHOP_SLOT", any.Body);
            }
        }

        if (_map.PurchaseRequest is not null &&
            string.Equals(key, _map.PurchaseRequest, StringComparison.Ordinal))
        {
            PurchaseRequestObservation? purchase = SemanticDecoders.TryDecodePurchaseRequest(any.Body);
            if (purchase is not null)
            {
                _pendingPurchaseRequest = purchase;
                _pendingPurchaseOffer = null;
            }
        }

        if (_map.PurchaseOffer is not null &&
            string.Equals(key, _map.PurchaseOffer, StringComparison.Ordinal))
        {
            PurchaseOfferObservation? offer = SemanticDecoders.TryDecodePurchaseOffer(any.Body);
            if (offer is not null)
                _pendingPurchaseOffer = offer;
        }

        if (_map.InventoryAdd is not null &&
            string.Equals(key, _map.InventoryAdd, StringComparison.Ordinal))
        {
            ItemDetailObservation? item = SemanticDecoders.TryDecodeInventoryAdd(any.Body);
            if (item is not null)
            {
                _itemDetails[item.ItemUid] = item;

                if (_pendingMarketWithdrawalUid is ulong marketListingUid &&
                    _marketListings.TryGetValue(marketListingUid, out var listing))
                {
                    ConsoleRenderer.WriteMarketListingReturn(
                        marketListingUid,
                        listing.Request,
                        listing.Created,
                        item);

                    _marketListings.Remove(marketListingUid);
                    _pendingMarketWithdrawalUid = null;
                }

                if (_pendingPurchaseRequest is not null)
                {
                    bool offerMatches =
                        _pendingPurchaseOffer is null ||
                        (_pendingPurchaseRequest.OfferId == _pendingPurchaseOffer.OfferId &&
                         _pendingPurchaseOffer.ItemId == item.ItemId);

                    if (offerMatches)
                        _pendingPurchasedItem = item;
                }
            }
        }

        if (_map.InventoryQuantity is not null &&
            string.Equals(key, _map.InventoryQuantity, StringComparison.Ordinal))
        {
            InventoryQuantityObservation? quantity =
                SemanticDecoders.TryDecodeInventoryQuantity(any.Body);

            if (quantity is not null)
            {
                ItemDetailObservation? updatedItem = null;

                if (_itemDetails.TryGetValue(quantity.ItemUid, out ItemDetailObservation? existingItem))
                {
                    updatedItem = existingItem with { Quantity = quantity.NewQuantity };
                }
                else if (_pendingPurchaseOffer is not null)
                {
                    updatedItem = new ItemDetailObservation(
                        quantity.ItemUid,
                        _pendingPurchaseOffer.ItemId,
                        quantity.NewQuantity,
                        _pendingPurchaseOffer.Stats);
                }

                if (updatedItem is not null)
                {
                    _itemDetails[quantity.ItemUid] = updatedItem;

                    if (_pendingPurchaseRequest is not null)
                        _pendingPurchasedItem = updatedItem;
                }
            }
        }

        if (_map.InventoryRemove is not null &&
            string.Equals(key, _map.InventoryRemove, StringComparison.Ordinal))
        {
            InventoryRemoveObservation? removed =
                SemanticDecoders.TryDecodeInventoryRemove(any.Body);

            if (removed is not null)
                _itemDetails.Remove(removed.ItemUid);
        }

        if (_map.PurchaseReceipt is not null &&
            string.Equals(key, _map.PurchaseReceipt, StringComparison.Ordinal))
        {
            PurchaseReceiptObservation? receipt =
                SemanticDecoders.TryDecodePurchaseReceipt(any.Body);

            if (receipt is not null &&
                _pendingPurchaseRequest is not null &&
                _pendingPurchasedItem is not null &&
                receipt.OfferId == _pendingPurchaseRequest.OfferId)
            {
                ConsoleRenderer.WritePurchase(
                    _pendingPurchaseRequest,
                    _pendingPurchasedItem,
                    receipt);

                _pendingPurchaseRequest = null;
                _pendingPurchaseOffer = null;
                _pendingPurchasedItem = null;
            }
        }

        if (_map.CraftPrepare is not null &&
            string.Equals(key, _map.CraftPrepare, StringComparison.Ordinal))
        {
            _craftIngredientSnapshots.Clear();
            _collectCraftIngredients = true;
            _activeCraftRequest = null;
        }

        if (_map.SmithmagicRequest is not null &&
            string.Equals(key, _map.SmithmagicRequest, StringComparison.Ordinal))
        {
            SmithmagicRequestObservation? request =
                SemanticDecoders.TryDecodeSmithmagicRequest(any.Body);

            if (request is not null)
                _pendingSmithmagicRequest = request;
        }

        if (_map.SmithmagicBatchRequest is not null &&
            string.Equals(key, _map.SmithmagicBatchRequest, StringComparison.Ordinal))
        {
            SmithmagicBatchRequestObservation? batch =
                SemanticDecoders.TryDecodeSmithmagicBatchRequest(any.Body);

            if (batch is not null)
            {
                if (_collectCraftIngredients && _craftIngredientSnapshots.Count > 0)
                {
                    _activeCraftRequest = new CraftRequestObservation(
                        batch.Quantity,
                        batch.Sequence,
                        _craftIngredientSnapshots.ToArray());

                    _collectCraftIngredients = false;
                    ConsoleRenderer.WriteCraftRequest(_activeCraftRequest);
                }
                else
                {
                    ulong runeUid = ResolveWorkshopRuneUid();
                    if (runeUid != 0)
                    {
                        _activeBatchSmithmagicRequest =
                            new SmithmagicRequestObservation(runeUid, batch.Quantity);
                        _pendingSmithmagicRequest = _activeBatchSmithmagicRequest;
                    }

                    Console.WriteLine(
                        $"[FM-BATCH] seq={batch.Sequence} x{batch.Quantity} " +
                        $"runeUID={(runeUid == 0 ? "?" : runeUid.ToString())}");
                }
            }
        }

        if (_map.SmithmagicAux is not null &&
            string.Equals(key, _map.SmithmagicAux, StringComparison.Ordinal))
        {
            SmithmagicStackObservation? stack =
                SemanticDecoders.TryDecodeSmithmagicStack(any.Body);

            if (stack is not null)
            {
                _itemDetails[stack.Stack.ItemUid] = stack.Stack;
                _workshopQuantities[stack.Stack.ItemUid] = checked((long)stack.Stack.Quantity);
                ConsoleRenderer.WriteSmithmagicStack(stack);
            }
            else
            {
                ConsoleRenderer.WriteProtoDebug($"FM_AUX {key}", any.Body);
            }
        }

        if (_map.CraftOutput is not null &&
            string.Equals(key, _map.CraftOutput, StringComparison.Ordinal) &&
            _activeCraftRequest is not null)
        {
            ItemDetailObservation? output =
                SemanticDecoders.TryDecodeCraftOutput(any.Body);

            if (output is not null)
            {
                _itemDetails[output.ItemUid] = output;
                Console.WriteLine(
                    $"[CRAFT-OUTPUT] ItemId={output.ItemId} UID={output.ItemUid} x{output.Quantity} | " +
                    $"stats: {(output.Stats.Count == 0 ? "(aucune stat)" : string.Join(", ", output.Stats.Select(x => $"{x.EffectId}={x.Value}")))}");
            }
            else
            {
                ConsoleRenderer.WriteProtoDebug("CRAFT_OUTPUT", any.Body);
            }
        }

        if (_map.SmithmagicResult is not null &&
            string.Equals(key, _map.SmithmagicResult, StringComparison.Ordinal))
        {
            SmithmagicResultObservation? result =
                SemanticDecoders.TryDecodeSmithmagicResult(any.Body);

            if (result is not null)
            {
                if (_activeCraftRequest is not null)
                {
                    ConsoleRenderer.WriteCraftResult(_activeCraftRequest, result);
                    _itemDetails[result.Item.ItemUid] = result.Item;
                    _activeCraftRequest = null;
                    _craftIngredientSnapshots.Clear();
                }
                else
                {
                    _itemDetails.TryGetValue(result.Item.ItemUid, out ItemDetailObservation? before);

                    SmithmagicRequestObservation? effectiveRequest =
                        _pendingSmithmagicRequest ?? _activeBatchSmithmagicRequest;

                    ItemDetailObservation? rune = null;
                    if (effectiveRequest is not null)
                        _itemDetails.TryGetValue(effectiveRequest.RuneUid, out rune);

                    ConsoleRenderer.WriteSmithmagic(
                        effectiveRequest,
                        rune,
                        before,
                        result);

                    _itemDetails[result.Item.ItemUid] = result.Item;
                    _activeSmithmagicTargetUid = result.Item.ItemUid;

                    if (_pendingSmithmagicRequest is not null &&
                        !ReferenceEquals(_pendingSmithmagicRequest, _activeBatchSmithmagicRequest))
                    {
                        _pendingSmithmagicRequest = null;
                    }
                    else if (_activeBatchSmithmagicRequest is null)
                    {
                        _pendingSmithmagicRequest = null;
                    }
                }
            }
        }

        if (_map.MarketListingRequest is not null &&
            string.Equals(key, _map.MarketListingRequest, StringComparison.Ordinal))
        {
            MarketListingRequestObservation? listing =
                SemanticDecoders.TryDecodeMarketListingRequest(any.Body);

            if (listing is not null)
                _pendingMarketListing = listing;
        }

        if (_map.MarketListingCreated is not null &&
            string.Equals(key, _map.MarketListingCreated, StringComparison.Ordinal))
        {
            MarketListingCreatedObservation? created =
                SemanticDecoders.TryDecodeMarketListingCreated(any.Body);

            if (created is not null && _pendingMarketListing is not null)
            {
                _itemDetails.TryGetValue(
                    _pendingMarketListing.ItemUid,
                    out ItemDetailObservation? knownItem);

                ConsoleRenderer.WriteMarketListing(
                    _pendingMarketListing,
                    created,
                    knownItem);

                _marketListings[created.MarketListingUid] =
                    (_pendingMarketListing, created);

                _pendingMarketListing = null;
            }
        }

        if (_map.DiagnosticMessages.Contains(key))
        {
            ConsoleRenderer.WriteProtoDebug($"DIAG {key}", any.Body);
        }

        if (_map.CrushResult is not null &&
            string.Equals(key, _map.CrushResult, StringComparison.Ordinal))
        {
            CrushObservation? crush = SemanticDecoders.TryDecodeCrush(any.Body);
            if (crush is not null)
                ConsoleRenderer.WriteCrush(crush, _itemDetails);
            else
            {
                Console.WriteLine($"[CRUSH?] {key} reçu mais structure non reconnue ({any.Body.Length} octets).");
                ConsoleRenderer.WriteProtoDebug("CRUSH", any.Body);
            }
        }
    }

    private ulong ResolveWorkshopRuneUid()
    {
        if (_lastWorkshopAddedUid is ulong last &&
            (!_activeSmithmagicTargetUid.HasValue || last != _activeSmithmagicTargetUid.Value))
        {
            return last;
        }

        foreach (ulong uid in _workshopQuantities.Keys.Reverse())
        {
            if (_activeSmithmagicTargetUid.HasValue &&
                uid == _activeSmithmagicTargetUid.Value)
            {
                continue;
            }

            return uid;
        }

        return 0;
    }

    public void Dispose()
    {
        _device.OnPacketArrival -= OnPacketArrival;
        _device.Dispose();
    }
}

internal enum FlowDirection
{
    ClientToServer,
    ServerToClient
}
