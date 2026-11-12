using System.Numerics;
using BestCrush.Network.Protocol;

namespace BestCrush.Services;

public sealed class LiveStockService(LiveStockRepository repository, CurrentDofusClientsState clients, LiveDofusClientNetworkBindings? bindings = null)
{
    private readonly object _sync = new();
    private readonly string _run = Guid.NewGuid().ToString("N");
    private readonly Dictionary<(long Epoch,long Connection), (string Id, LiveStockBook Book, DofusClientWindowKey Window)> _flows = [];
    private readonly Dictionary<DofusClientWindowKey, string> _windows = [];
    private readonly Dictionary<string,LiveStockSource> _sources = [];
    private long _epoch = -1;
    private readonly Dictionary<DofusClientWindowKey,long> _latestConnections = [];
    public event Action? Changed;
    public IReadOnlyList<LiveStockSource> History => repository.ReadSources();
    public LiveStockSource? Selected => clients.SelectedClient is { } client ? ForWindow(client.Key) : null;
    internal LiveStockSource? SelectedForServer(string? server)
    {
        var source = Selected;
        return server is not null && string.Equals(source?.ServerName, server, StringComparison.OrdinalIgnoreCase) ? source : null;
    }

    // Read each live window independently so header indicators never borrow the selected character’s stock.
    public LiveStockSource? ForWindow(DofusClientWindowKey window)
    {
        if (!clients.Clients.Any(client => client.Key == window)) return null;
        lock (_sync)
        {
            if (!_windows.TryGetValue(window, out string? id)) return null;
            if (bindings is not null && (!_latestConnections.TryGetValue(window, out long connection) ||
                !bindings.IsCurrentConnection(_epoch, connection, window))) return null;
            return _sources.GetValueOrDefault(id);
        }
    }
    internal void Observe(DofusWireMessage message)
    {
        if (message.Direction != "S→C" || message.ConnectionId <= 0 || message.CandidateWindow is not { } window ||
            !LiveStockProtocol.IsStockOpcode(message.Key)) return;
        lock (_sync)
        {
            if (_epoch != message.CaptureEpoch)
            {
                _flows.Clear(); _windows.Clear(); _sources.Clear(); _latestConnections.Clear(); _epoch = message.CaptureEpoch;
            }
            if (_latestConnections.TryGetValue(window,out long latest) && message.ConnectionId < latest) return;
            _latestConnections[window] = message.ConnectionId;
            var key = (message.CaptureEpoch,message.ConnectionId);
            if (!_flows.TryGetValue(key,out var flow))
            {
                if (_flows.Count >= 128) return;
                flow = ($"{_run}-{message.CaptureEpoch}-{message.ConnectionId}", new LiveStockBook(),window);
                _flows[key] = flow;
            }
            if (flow.Window != window) return;
            var state = flow.Book.Observe(message.Key,message.Body,message.ObservedAtUtc);
            if (state is null) return;
            string label = clients.Clients.FirstOrDefault(x=>x.Key==window)?.DisplayName ?? "Fenêtre Dofus";
            string? server = message.CaptureLease?.ServerName;
            repository.Save(flow.Id,label,server,state);
            _windows[window]=flow.Id;
            _sources[flow.Id]=new(flow.Id,label,server ?? _sources.GetValueOrDefault(flow.Id)?.ServerName,
                message.ObservedAtUtc,flow.Book.Containers);
        }
        Changed?.Invoke();
    }
    public BigInteger? Quantity(long itemId, string container, string? server = null) =>
        (server is null ? Selected : SelectedForServer(server))?.QuantityIn(itemId, container);
    public void ClearLive()
    {
        lock(_sync) { _flows.Clear();_windows.Clear();_sources.Clear();_latestConnections.Clear(); }
        Changed?.Invoke();
    }
}
