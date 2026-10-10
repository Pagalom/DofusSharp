namespace BestCrush.Services;

/// <summary>
/// Snapshot of the server chosen by the user when a network message arrived.
/// The protocol itself does not verify this server name.
/// </summary>
public readonly record struct NetworkCaptureLease(string ServerName, long Generation);

public sealed class CurrentServerState
{
    private readonly object _sync = new();
    private string? _serverName;
    private long _generation;

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
        lock (_sync)
            return _serverName is not null
                ? new NetworkCaptureLease(_serverName, _generation)
                : null;
    }

    /// <summary>
    /// Ignore old queued messages when the selected server changes.
    /// This does not verify the Dofus server; selection is user-authoritative.
    /// </summary>
    public bool IsCaptureLeaseActive(NetworkCaptureLease lease)
    {
        lock (_sync)
            return _generation == lease.Generation &&
                string.Equals(_serverName, lease.ServerName, StringComparison.Ordinal);
    }
}
