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

    private PurchaseRequestObservation? _pendingPurchaseRequest;
    private PurchaseOfferObservation? _pendingPurchaseOffer;
    private SmithmagicRequestObservation? _pendingSmithmagicRequest;
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

                if (_pendingPurchaseRequest is not null &&
                    _pendingPurchaseOffer is not null &&
                    _pendingPurchaseRequest.ListingId == _pendingPurchaseOffer.ListingId &&
                    _pendingPurchaseOffer.ItemId == item.ItemId)
                {
                    ConsoleRenderer.WritePurchase(
                        _pendingPurchaseRequest,
                        _pendingPurchaseOffer,
                        item);

                    _pendingPurchaseRequest = null;
                    _pendingPurchaseOffer = null;
                }
            }
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
                ulong runeUid = ResolveWorkshopRuneUid();
                if (runeUid != 0)
                    _pendingSmithmagicRequest = new SmithmagicRequestObservation(runeUid, batch.Quantity);

                Console.WriteLine(
                    $"[FM-BATCH] seq={batch.Sequence} x{batch.Quantity} " +
                    $"runeUID={(runeUid == 0 ? "?" : runeUid.ToString())}");
            }
        }

        if (_map.SmithmagicAux is not null &&
            string.Equals(key, _map.SmithmagicAux, StringComparison.Ordinal))
        {
            ConsoleRenderer.WriteProtoDebug($"FM_AUX {key}", any.Body);
        }

        if (_map.SmithmagicResult is not null &&
            string.Equals(key, _map.SmithmagicResult, StringComparison.Ordinal))
        {
            SmithmagicResultObservation? result =
                SemanticDecoders.TryDecodeSmithmagicResult(any.Body);

            if (result is not null)
            {
                _itemDetails.TryGetValue(result.Item.ItemUid, out ItemDetailObservation? before);

                ItemDetailObservation? rune = null;
                if (_pendingSmithmagicRequest is not null)
                    _itemDetails.TryGetValue(_pendingSmithmagicRequest.RuneUid, out rune);

                ConsoleRenderer.WriteSmithmagic(
                    _pendingSmithmagicRequest,
                    rune,
                    before,
                    result);

                _itemDetails[result.Item.ItemUid] = result.Item;
                _activeSmithmagicTargetUid = result.Item.ItemUid;
                _pendingSmithmagicRequest = null;
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
