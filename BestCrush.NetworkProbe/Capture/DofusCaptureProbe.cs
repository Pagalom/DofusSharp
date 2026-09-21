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
                Console.WriteLine($"[MARKET?] {key} reçu mais structure non reconnue ({any.Body.Length} octets).");
        }

        if (_map.CrushResult is not null &&
            string.Equals(key, _map.CrushResult, StringComparison.Ordinal))
        {
            CrushObservation? crush = SemanticDecoders.TryDecodeCrush(any.Body);
            if (crush is not null)
                ConsoleRenderer.WriteCrush(crush);
            else
                Console.WriteLine($"[CRUSH?] {key} reçu mais structure non reconnue ({any.Body.Length} octets).");
        }
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
