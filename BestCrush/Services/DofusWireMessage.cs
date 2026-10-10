namespace BestCrush.Services;

internal sealed record DofusWireMessage(
    string Direction,
    string Key,
    byte[] Body,
    DateTime ObservedAtUtc,
    TaskCompletionSource<bool>? Completion = null);
