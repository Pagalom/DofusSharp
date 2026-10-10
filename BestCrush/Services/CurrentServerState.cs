namespace BestCrush.Services;

/// <summary>
/// User-confirmed attribution. The decoded protocol does not identify the
/// actual Dofus server; confirmation is an explicit user assertion.
/// </summary>
public readonly record struct NetworkCaptureLease(string ServerName, long Generation);
public readonly record struct NetworkCaptureStatus(
    string? ServerName, bool IsConfirmed, bool IsAmbiguous);

public sealed class CurrentServerState
{
    private readonly object _sync = new();
    private string? _serverName;
    private long _generation;
    private bool _confirmed;
    private bool _ambiguous;
    private string? _connectionId;

    public event Action? CaptureAuthorizationChanged;

    public string? ServerName
    {
        get { lock (_sync) return _serverName; }
    }

    public bool HasSelectedServer =>
        !string.IsNullOrWhiteSpace(ServerName);

    public NetworkCaptureStatus GetCaptureStatus()
    {
        lock (_sync)
            return new(_serverName, _confirmed, _ambiguous);
    }

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
            RevokeInsideLock(ambiguous: false);
        }

        CaptureAuthorizationChanged?.Invoke();
    }

    /// <summary>Explicit user action, never navigation-driven.</summary>
    public void ConfirmCapture(string serverName)
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(_serverName) ||
                !string.Equals(_serverName, serverName, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Le serveur sélectionné a changé. Confirmez le nouveau serveur.");

            _generation++;
            _confirmed = true;
            _ambiguous = false;
            _connectionId = null;
        }

        CaptureAuthorizationChanged?.Invoke();
    }

    public void SuspendCapture()
    {
        lock (_sync)
            RevokeInsideLock(ambiguous: false);

        CaptureAuthorizationChanged?.Invoke();
    }

    public void Clear()
    {
        lock (_sync)
        {
            _serverName = null;
            RevokeInsideLock(ambiguous: false);
        }

        CaptureAuthorizationChanged?.Invoke();
    }

    public NetworkCaptureLease? GetCaptureLease()
    {
        lock (_sync)
            return _confirmed && _serverName is not null
                ? new NetworkCaptureLease(_serverName, _generation)
                : null;
    }

    public bool IsCaptureLeaseActive(NetworkCaptureLease lease)
    {
        lock (_sync)
            return IsActiveInsideLock(lease);
    }

    /// <summary>
    /// One TCP connection per confirmation; a second observed connection
    /// suspends capture instead of guessing which game client to trust.
    /// </summary>
    public bool TryAcceptConnection(NetworkCaptureLease lease, string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
            return false;

        bool ambiguous = false;
        lock (_sync)
        {
            if (!IsActiveInsideLock(lease))
                return false;

            if (_connectionId is null)
            {
                _connectionId = connectionId;
                return true;
            }

            if (string.Equals(_connectionId, connectionId, StringComparison.Ordinal))
                return true;

            RevokeInsideLock(ambiguous: true);
            ambiguous = true;
        }

        if (ambiguous)
            CaptureAuthorizationChanged?.Invoke();

        return false;
    }

    private bool IsActiveInsideLock(NetworkCaptureLease lease) =>
        _confirmed &&
        _generation == lease.Generation &&
        string.Equals(_serverName, lease.ServerName, StringComparison.Ordinal);

    private void RevokeInsideLock(bool ambiguous)
    {
        _generation++;
        _confirmed = false;
        _ambiguous = ambiguous;
        _connectionId = null;
    }
}
