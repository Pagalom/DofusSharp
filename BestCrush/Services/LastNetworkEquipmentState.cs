namespace BestCrush.Services;

public sealed record LastNetworkEquipmentSnapshot(
    long DofusDbId,
    string ServerName,
    DateTime ObservedAtUtc
);

/// <summary>
/// Remembers the most recent equipment identified with certainty from
/// passive Dofus network traffic. This is intentionally independent from
/// FocusedEquipmentState: network traffic observes game activity, while
/// focus changes only after an explicit user action (middle click, F8, UI).
/// </summary>
public sealed class LastNetworkEquipmentState
{
    private readonly object _sync = new();
    private LastNetworkEquipmentSnapshot? _snapshot;

    public void Set(
        long dofusDbId,
        string serverName,
        DateTime observedAtUtc)
    {
        if (dofusDbId <= 0 ||
            string.IsNullOrWhiteSpace(serverName))
        {
            return;
        }

        lock (_sync)
        {
            _snapshot =
                new LastNetworkEquipmentSnapshot(
                    dofusDbId,
                    serverName,
                    observedAtUtc
                );
        }
    }

    public LastNetworkEquipmentSnapshot?
        GetForServer(
            string? serverName)
    {
        if (string.IsNullOrWhiteSpace(
            serverName))
        {
            return null;
        }

        lock (_sync)
        {
            if (_snapshot is null ||
                !string.Equals(
                    _snapshot.ServerName,
                    serverName,
                    StringComparison.Ordinal))
            {
                return null;
            }

            return _snapshot;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _snapshot = null;
        }
    }
}
