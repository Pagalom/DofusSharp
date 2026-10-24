using BestCrush.Network.Protocol;

namespace BestCrush.Services;

/// <summary>
/// Opt-in, Debug-only live study. Reports whether profession messages arrive
/// on an independently authorized Game TCP connection/window. It never
/// infers an Ankama account, publishes job data, or writes to SQLite.
/// Window keys remain RAM-only; logs expose anonymous ordinal slots.
/// </summary>
internal sealed class ProfessionGameLinkStudy
{
    private const int MaximumWindows = 16;
    private const int MaximumReports = 128;
    private readonly Dictionary<DofusClientWindowKey, int> _slots = [];
    private long _captureEpoch = -1;
    private int _reports;

    internal string? Observe(DofusWireMessage message, bool activeGameLease)
    {
        if (message.Key != "ipz" || message.Direction != "S→C")
            return null;

        if (message.CaptureEpoch < _captureEpoch)
            return null;

        if (message.CaptureEpoch != _captureEpoch)
        {
            _slots.Clear();
            _reports = 0;
            _captureEpoch = message.CaptureEpoch;
        }

        if (_reports++ == MaximumReports)
            return "p3d=report-limit account=unverified";

        if (_reports > MaximumReports)
            return null;

        // Structural roster decoding is separate from account and job-ID proof.
        // No parsed level, XP, job candidate, account or payload is formatted.
        string shape = ProfessionIpzDecoder.TryDecode(message.Body, out _)
            ? "roster20"
            : "other";

        bool verifiedGame =
            activeGameLease &&
            message.CandidateWindow is { } window &&
            message.CaptureLease is { } lease &&
            lease.Window == window &&
            lease.CaptureEpoch == message.CaptureEpoch &&
            lease.ConnectionId == message.ConnectionId &&
            lease.ConnectionId > 0 &&
            !string.IsNullOrWhiteSpace(lease.ServerName);

        if (!verifiedGame)
            return $"p3d=ipz link=unverified slot=none server=unverified shape={shape} account=unverified mapping=unverified";

        DofusClientWindowKey verifiedWindow = message.CandidateWindow!.Value;
        if (!_slots.TryGetValue(verifiedWindow, out int slot))
        {
            if (_slots.Count >= MaximumWindows)
                return $"p3d=ipz link=slot-limit server=redacted shape={shape} account=unverified mapping=unverified";
            slot = _slots.Count + 1;
            _slots.Add(verifiedWindow, slot);
        }

        return $"p3d=ipz link=verified-game slot={slot} server={message.CaptureLease!.Value.ServerName} shape={shape} account=unverified mapping=unverified";
    }
}
