namespace BestCrush.Services;

/// <summary>
/// Snapshot of the server chosen by the user when a network message arrived.
/// The protocol itself does not verify this server name.
/// </summary>
public readonly record struct NetworkCaptureLease(
    string ServerName,
    long Generation,
    DofusClientWindowKey? Window = null,
    long CaptureEpoch = -1,
    long ConnectionId = 0);

public sealed class CurrentServerState
{
    private readonly object _sync = new();
    private string? _serverName;
    private long _generation;

    // Auto mode is explicitly enabled by production DI only.
    // Replay fixtures that construct CurrentServerState() retain the
    // historical manual contract and its existing regression tests.
    private readonly WindowServerAttributionState? _automaticAttributions;

    public CurrentServerState(
        WindowServerAttributionState? automaticAttributions = null,
        bool automaticRoutingEnabled = false)
    {
        _automaticAttributions = automaticAttributions;
        AutomaticRoutingEnabled = automaticRoutingEnabled &&
            automaticAttributions is not null;
    }

    public bool AutomaticRoutingEnabled { get; }

    public event Action? ServerSelectionChanged;

    public string? ServerName
    {
        get
        {
            lock (_sync)
                return _serverName;
        }
    }

    public bool HasSelectedServer =>
        !string.IsNullOrWhiteSpace(ServerName);

    /// <summary>
    /// Choosing a server immediately enables capture for that server.
    /// Re-selecting the same name leaves the current generation intact.
    /// </summary>
    public void SelectServer(string serverName)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            throw new ArgumentException(
                "Le nom du serveur ne peut pas être vide.", nameof(serverName));

        lock (_sync)
        {
            if (string.Equals(_serverName, serverName, StringComparison.Ordinal))
                return;

            _serverName = serverName;
            _generation++;
        }

        ServerSelectionChanged?.Invoke();
    }

    public void Clear()
    {
        lock (_sync)
        {
            _serverName = null;
            _generation++;
        }

        ServerSelectionChanged?.Invoke();
    }

    public NetworkCaptureLease? GetCaptureLease()
    {
        // The historical global UI server is never an authorization to
        // persist live packets in automatic multi-server mode.
        if (AutomaticRoutingEnabled)
            return null;

        lock (_sync)
            return _serverName is not null
                ? new NetworkCaptureLease(_serverName, _generation)
                : null;
    }

    internal NetworkCaptureLease? GetGameCaptureLease(
        DofusClientWindowKey window, long captureEpoch, long connectionId) =>
        AutomaticRoutingEnabled
            ? _automaticAttributions?.GetGameLease(
                window, captureEpoch, connectionId)
            : GetCaptureLease();

    /// <summary>
    /// Ignore old queued messages when the selected server changes.
    /// This does not verify the Dofus server; selection is user-authoritative.
    /// </summary>
    public NetworkCaptureLease? GetWindowCaptureLease(
        DofusClientWindowKey window) =>
        AutomaticRoutingEnabled
            ? _automaticAttributions?.GetWindowLease(window)
            : GetCaptureLease();

    public bool IsCaptureLeaseActive(NetworkCaptureLease lease)
    {
        if (AutomaticRoutingEnabled)
            return _automaticAttributions?.IsGameLeaseActive(lease) == true;

        // In the legacy single-server/test mode, reject an automatic lease.
        if (lease.Window is not null)
            return false;

        lock (_sync)
            return _generation == lease.Generation &&
                string.Equals(_serverName, lease.ServerName, StringComparison.Ordinal);
    }
}
