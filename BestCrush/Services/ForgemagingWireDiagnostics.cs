using System.Text.Json;

namespace BestCrush.Services;

/// <summary>Short-lived, opt-in local diagnostics for the workshop currently followed in game.</summary>
internal static class ForgemagingWireDiagnostics
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BestCrush", "DebugCaptures", "Forgemaging");
    internal static string SwitchPath => Path.Combine(Folder, "enabled");
    internal static bool IsWorkshopMessage(string key) => key is "jys" or "jxu" or "kco" or "kch" or "kbr" or "jyz" or "irx" or "iqy" or "iqm";

    internal static async Task WriteIfEnabledAsync(DofusWireMessage message, CancellationToken token)
    {
        if (!IsWorkshopMessage(message.Key) || message.Body.Length > 16384 || !File.Exists(SwitchPath) ||
            DateTime.UtcNow - File.GetLastWriteTimeUtc(SwitchPath) > TimeSpan.FromMinutes(30)) return;
        string file = Path.Combine(Folder, "workshop.jsonl");
        if (File.Exists(file) && new FileInfo(file).Length >= 2 * 1024 * 1024) return;
        try
        {
            await File.AppendAllTextAsync(file, JsonSerializer.Serialize(new { utc = message.ObservedAtUtc,
                direction = message.Direction, key = message.Key, bodyBase64 = Convert.ToBase64String(message.Body) }) + Environment.NewLine, token);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
