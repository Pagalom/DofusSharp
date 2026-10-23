namespace BestCrush.Services;

/// <summary>
/// P3c.11 presentation-only candidate window information, never a confirmed
/// character or account. Window is a local HWND/PID key; the only wire-derived
/// value exposed to Blazor is the bounded number of candidate roster members.
/// Do not log/serialize a card (including its window handle).
/// </summary>
public sealed record CandidateWindowRosterCard(
    DofusClientWindowKey Window, int CandidateCount,
    int? SelectedLevelCandidate = null)
{
    public override string ToString() =>
        "[candidate window card: redacted account=unverified]";
}

/// <summary>
/// Process-lifetime only. Deliberately not active in normal BestCrush:
/// the network-candidate identity is still exploratory. Activated ONLY by
/// the existing explicit P3c.10 Debug opt-in, never by app startup or DI.
/// The Win32 polling loop drives expiry even if there are no TCP packets.
/// </summary>
public sealed class CurrentCandidateWindowCardsState
{
    private readonly object _sync = new();
    private EphemeralCandidateRosterWireObserver? _observer;

    public event Action? Changed;

    public bool IsResearchEnabled
    {
        get { lock (_sync) return _observer is not null; }
    }

    public IReadOnlyList<CandidateWindowRosterCard> Cards
    {
        get
        {
            EphemeralCandidateRosterWireObserver? observer;
            lock (_sync) observer = _observer;
            return observer?.ReadWindowCards() ?? [];
        }
    }

    public bool HasCandidateLink(DofusClientWindowKey window) =>
        Cards.Any(card => card.Window == window);

    /// <summary>
    /// Explicit Debug-only activation by DofusNetworkCaptureService after
    /// it verifies the environment gate and local OS owner services.
    /// Returning the same observer for duplicate service creation avoids
    /// unintentionally clearing an existing session's candidates.
    /// </summary>
    internal EphemeralCandidateRosterWireObserver EnableForResearch(
        CurrentServerState serverState)
    {
        ArgumentNullException.ThrowIfNull(serverState);
        EphemeralCandidateRosterWireObserver observer;
        bool created = false;
        lock (_sync)
        {
            if (_observer is null)
            {
                _observer = new EphemeralCandidateRosterWireObserver(
                    serverState, new EphemeralCandidateRosterGroupsState());
                created = true;
            }
            observer = _observer;
        }

        if (created)
            Changed?.Invoke();
        return observer;
    }

    /// <summary>
    /// Called by the worker after a ksc/ksb observation or capture/lease
    /// reset. Events contain no payloads and the UI reads fresh snapshots.
    /// </summary>
    internal void NotifyChanged()
    {
        if (IsResearchEnabled)
            Changed?.Invoke();
    }

    /// <summary>
    /// One-second independent Win32 inventory, NOT a TCP packet callback.
    /// A closed window and its pending/linked candidates are revoked.
    /// This is a strict replacement inventory, so never invoke it using
    /// an untrusted or partial window list.
    /// </summary>
    public void RemoveMissingWindows(
        IReadOnlyCollection<DofusClientWindowKey> liveWindows)
    {
        ArgumentNullException.ThrowIfNull(liveWindows);
        EphemeralCandidateRosterWireObserver? observer;
        lock (_sync) observer = _observer;
        if (observer is null)
            return;

        IReadOnlyList<CandidateWindowRosterCard> before =
            observer.ReadWindowCards();
        observer.ReconcileWindows(liveWindows);
        IReadOnlyList<CandidateWindowRosterCard> after =
            observer.ReadWindowCards();

        if (!before.SequenceEqual(after))
            Changed?.Invoke();
    }
}
