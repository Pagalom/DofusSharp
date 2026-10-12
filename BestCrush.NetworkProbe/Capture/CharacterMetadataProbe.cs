using BestCrush.Network.Capture;
using PacketDotNet;
using SharpPcap;

namespace BestCrush.NetworkProbe.Capture;

/// <summary>
/// Isolated metadata-only protocol discovery. Does not run semantic market
/// decoders or write raw captures, payloads, endpoints or character details.
/// A per-client TCP tracker is required to avoid cross-account mixing.
/// </summary>
internal sealed class CharacterMetadataProbe(
    ICaptureDevice device, int port) : IDisposable
{
    private readonly CharacterCaptureFlowTracker _flows = new();

    public void Start()
    {
        device.OnPacketArrival += OnPacketArrival;
        try
        {
            device.Open(DeviceModes.Promiscuous, 1000);
            device.Filter = $"tcp port {port}";
            device.StartCapture();
        }
        catch
        {
            device.OnPacketArrival -= OnPacketArrival;
            throw;
        }
    }

    public void Stop()
    {
        try
        {
            device.StopCapture();
        }
        catch
        {
            // Capture shutdown is best-effort.
        }

        device.OnPacketArrival -= OnPacketArrival;
    }

    private void OnPacketArrival(object sender, PacketCapture capture)
    {
        try
        {
            RawCapture raw = capture.GetPacket();
            Packet packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
            IPPacket? ip = packet.Extract<IPPacket>();
            TcpPacket? tcp = packet.Extract<TcpPacket>();

            if (ip is null || tcp is null ||
                (tcp.SourcePort != port && tcp.DestinationPort != port))
                return;

            // No address or port is ever included in emitted metadata.
            bool incoming = tcp.SourcePort == port;
            CharacterCaptureFlowKey key = incoming
                ? new CharacterCaptureFlowKey(
                    ip.DestinationAddress.ToString(), tcp.DestinationPort,
                    ip.SourceAddress.ToString(), tcp.SourcePort)
                : new CharacterCaptureFlowKey(
                    ip.SourceAddress.ToString(), tcp.SourcePort,
                    ip.DestinationAddress.ToString(), tcp.DestinationPort);

            IReadOnlyList<CharacterCaptureEvent> events = _flows.Observe(
                key,
                incoming
                    ? CharacterCaptureDirection.ServerToClient
                    : CharacterCaptureDirection.ClientToServer,
                tcp.SequenceNumber,
                tcp.PayloadData,
                DateTime.UtcNow,
                syn: tcp.Synchronize,
                fin: tcp.Finished,
                rst: tcp.Reset);

            foreach (CharacterCaptureEvent entry in events)
            {
                string direction = entry.Direction ==
                    CharacterCaptureDirection.ServerToClient ? "S→C" : "C→S";

                string detail = entry.Kind == CharacterCaptureEventKind.Message
                    ? $"{entry.Opcode} {entry.BodyLength} octets"
                    : entry.Kind.ToString().ToUpperInvariant();

                Console.WriteLine(
                    $"[{entry.ObservedAtUtc:HH:mm:ss.fff} UTC] " +
                    $"Session #{entry.SessionNumber} {direction} {detail}");
            }
        }
        catch (Exception ex)
        {
            // Deliberately never print packet data, exceptions' captured
            // byte contents, TCP endpoint details, or stack traces.
            Console.Error.WriteLine(
                $"[metadata probe] Paquet ignoré : {ex.GetType().Name}.");
        }
    }

    public void Dispose()
    {
        Stop();
        device.Dispose();
    }
}
