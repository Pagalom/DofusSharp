namespace BestCrush.Services;

internal sealed record DofusWireMessage(
    string Direction,
    string Key,
    byte[] Body,
    DateTime ObservedAtUtc,
    NetworkCaptureLease? CaptureLease,
    TaskCompletionSource<bool>? Completion = null);
