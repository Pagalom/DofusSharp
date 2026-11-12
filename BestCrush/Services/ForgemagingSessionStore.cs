using System.Text.Json;
using BestCrush.Domain.Models;

namespace BestCrush.Services;

/// <summary>Independent local sessions per server and item; observations and stock are never written.</summary>
public sealed class ForgemagingSessionStore(string path)
{
    private readonly object _sync = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public sealed class SavedSessions
    {
        public Dictionary<string, long> LastItems { get; set; } = [];
        public Dictionary<string, ForgemagingSession> Sessions { get; set; } = [];
    }
    private SavedSessions Read()
    {
        if (!File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<SavedSessions>(File.ReadAllText(path), Json) ?? throw new JsonException(); }
        catch (JsonException error) { throw new InvalidDataException("Le fichier des sessions FM est illisible. Il est conservé.", error); }
    }
    private static string Key(string server, long id, string? recording = null) => $"{server.ToUpperInvariant()}:{id}" + (recording is null ? "" : $":game:{recording}");
    public ForgemagingSession? Get(string server, long? itemId = null)
    {
        lock (_sync)
        {
            var saved = Read();
            long id = itemId ?? saved.LastItems.GetValueOrDefault(server.ToUpperInvariant());
            return saved.Sessions.GetValueOrDefault(Key(server, id));
        }
    }
    public void Save(string server, ForgemagingSession session)
    {
        if (string.IsNullOrWhiteSpace(server) || session.ItemId <= 0) throw new ArgumentException("Session invalide.");
        lock (_sync)
        {
            var saved = Read();
            if (session.GameRecordingId is null) saved.LastItems[server.ToUpperInvariant()] = session.ItemId;
            saved.Sessions[Key(server, session.ItemId, session.GameRecordingId)] = session;
            string json = JsonSerializer.Serialize(saved, Json);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, json); File.Move(temporary, path, overwrite: true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    public static ForgemagingSession Copy(ForgemagingSession session) =>
        JsonSerializer.Deserialize<ForgemagingSession>(JsonSerializer.Serialize(session, Json), Json)!;
}
