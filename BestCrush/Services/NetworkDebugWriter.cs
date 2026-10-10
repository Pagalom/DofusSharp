using System.Text.Json;
using BestCrush.Network.Protocol;

namespace BestCrush.Services;

/// <summary>
/// File diagnostics for passive network ingestion.
/// Debug I/O stays in the same place in the processing sequence.
/// </summary>
internal sealed class NetworkDebugWriter(
    CurrentServerState currentServerState,
    Func<bool> keepDebugArtifacts,
    int dofusPort)
{
    private ProtocolMap? protocolMap;

    private string? _debugSessionDirectory;
    private string? _wireDebugPath;
    private string? _eventsDebugPath;

    internal void SetProtocolMap(
        ProtocolMap? map)
    {
        protocolMap = map;
    }

    internal async Task WriteWireDebugAsync(
        DofusWireMessage message,
        CancellationToken cancellationToken)
    {
        if (!keepDebugArtifacts())
        {
            return;
        }

        EnsureDebugSessionDirectory();

        if (_wireDebugPath is null)
        {
            return;
        }

        string json =
            JsonSerializer.Serialize(
                new
                {
                    utc =
                        message.ObservedAtUtc,
                    direction =
                        message.Direction,
                    key =
                        message.Key,
                    bodyLength =
                        message.Body.Length,
                    bodyBase64 =
                        Convert.ToBase64String(
                            message.Body)
                }
            );

        await File.AppendAllTextAsync(
            _wireDebugPath,
            json + Environment.NewLine,
            cancellationToken);
    }

    internal async Task WriteEventDebugAsync(
        DateTime observedAtUtc,
        string text,
        CancellationToken cancellationToken)
    {
        if (!keepDebugArtifacts())
        {
            return;
        }

        EnsureDebugSessionDirectory();

        if (_eventsDebugPath is null)
        {
            return;
        }

        await File.AppendAllTextAsync(
            _eventsDebugPath,
            $"[{observedAtUtc:O}] {text}" +
            Environment.NewLine,
            cancellationToken);
    }

    private void EnsureDebugSessionDirectory()
    {
        if (_debugSessionDirectory is not null)
        {
            return;
        }

        string root =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "BestCrush",
                "DebugCaptures",
                "Network");

        Directory.CreateDirectory(root);

        string sessionName =
            $"session-{DateTime.Now:yyyyMMdd-HHmmss}-" +
            $"{Guid.NewGuid():N}";

        _debugSessionDirectory =
            Path.Combine(
                root,
                sessionName);

        Directory.CreateDirectory(
            _debugSessionDirectory);

        _wireDebugPath =
            Path.Combine(
                _debugSessionDirectory,
                "wire.jsonl");

        _eventsDebugPath =
            Path.Combine(
                _debugSessionDirectory,
                "events.log");

        string metadataPath =
            Path.Combine(
                _debugSessionDirectory,
                "session.txt");

        File.WriteAllText(
            metadataPath,
            string.Join(
                Environment.NewLine,
                [
                    "BESTCRUSH NETWORK DEBUG",
                    $"CreatedLocal: {DateTime.Now:O}",
                    $"ClientBuild: {protocolMap?.ClientBuild ?? "unknown"}",
                    $"TCP port: {dofusPort}",
                    $"Server: {currentServerState.ServerName ?? "(not selected)"}",
                    "",
                    "wire.jsonl = exact decoded Ankama Any bodies (Base64), one message per line.",
                    "events.log = human-readable semantic events decoded by BestCrush."
                ]
            )
        );
    }


}
