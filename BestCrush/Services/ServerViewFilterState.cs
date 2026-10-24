namespace BestCrush.Services;

/// <summary>
/// In-memory server used ONLY to browse SQL-backed prices, coefficients and
/// history. Never an authority for Dofus TCP attribution or SQLite writes.
/// The user can temporarily inspect any server; explicit character targeting
/// or a real Dofus foreground activation returns to that character's server.
/// </summary>
public sealed class ServerViewFilterState
{
    private string? _serverName;
    private bool _browsingManually;
    private DofusClientWindowKey? _targetWindow;

    public event Action? Changed;

    public string? ServerName => _serverName;
    public bool IsBrowsingManually => _browsingManually;
    public DofusClientWindowKey? TargetWindow => _targetWindow;

    public bool BrowseServer(string serverName)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            throw new ArgumentException("Serveur de consultation requis.", nameof(serverName));

        bool changed = !_browsingManually ||
            !string.Equals(_serverName, serverName, StringComparison.OrdinalIgnoreCase);
        _browsingManually = true;
        _serverName = serverName;
        if (changed) Changed?.Invoke();
        return changed;
    }

    /// <summary>A click on a character card (even the already targeted card)
    /// or actual game-window activation revokes manual browsing immediately.</summary>
    public bool FollowWindow(DofusClientWindowKey? window, string? networkServer)
    {
        bool changed = _browsingManually ||
            _targetWindow != window ||
            !string.Equals(_serverName, networkServer,
                StringComparison.OrdinalIgnoreCase);
        _browsingManually = false;
        _targetWindow = window;
        _serverName = networkServer;
        if (changed) Changed?.Invoke();
        return changed;
    }

    /// <summary>Network discoveries may update the default view, but never
    /// steal an explicit SQL browsing filter. They may not alter identities.</summary>
    public bool ReconcileTarget(DofusClientWindowKey? window, string? networkServer)
    {
        if (_browsingManually)
            return false;
        return FollowWindow(window, networkServer);
    }
}
