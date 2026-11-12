using System.Text.Json;
using BestCrush.Domain.Models;

namespace BestCrush.Services;

/// <summary>Local sessions per server, with separate manual and observed equipment histories.</summary>
public sealed class ForgemagingSessionStore(string path)
{
    private readonly object _sync = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal event Action<string>? GameHistoryChanged;
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
    internal ForgemagingSession? GetObserved(string server, long itemId, string key)
    {
        lock (_sync) return Read().Sessions.GetValueOrDefault(Key(server, itemId, key));
    }

    internal Dictionary<ulong, long> FindObservedItemIds(string server, IEnumerable<ulong> uids)
    {
        lock (_sync)
        {
            var saved = Read().Sessions;
            string prefix = server.ToUpperInvariant() + ":";
            var itemIds = saved.Where(entry => entry.Key.StartsWith(prefix, StringComparison.Ordinal) &&
                    entry.Value.GameRecordingId is not null)
                .Select(entry => entry.Value.ItemId).Distinct().ToArray();
            var result = new Dictionary<ulong, long>();
            foreach (ulong uid in uids.Distinct())
            {
                var matches = itemIds.Where(id => saved.ContainsKey(Key(server, id,
                    ForgemagedEquipmentStock.InstanceKey(server, id, uid)))).Take(2).ToArray();
                if (matches.Length == 1) result[uid] = matches[0];
            }
            return result;
        }
    }

    internal Dictionary<string, ForgemagingSession> GetObserved(string server, IEnumerable<(long ItemId, string Key)> items)
    {
        lock (_sync)
        {
            var saved = Read();
            var result = new Dictionary<string, ForgemagingSession>();
            foreach (var item in items)
                if (saved.Sessions.TryGetValue(Key(server, item.ItemId, item.Key), out var session))
                    result[item.Key] = session;
            return result;
        }
    }

    public void Save(string server, ForgemagingSession session)
    {
        if (string.IsNullOrWhiteSpace(server) || session.ItemId <= 0) throw new ArgumentException("Session invalide.");
        bool historyChanged = false;
        lock (_sync)
        {
            var saved = Read();
            if (session.GameRecordingId is null) saved.LastItems[server.ToUpperInvariant()] = session.ItemId;
            string key = Key(server, session.ItemId, session.GameRecordingId);
            if (session.GameRecordingId is not null)
            {
                var previous = saved.Sessions.GetValueOrDefault(key);
                historyChanged = previous?.Attempts.Count != session.Attempts.Count ||
                    previous?.LastObservedRollAtUtc != session.LastObservedRollAtUtc;
            }
            saved.Sessions[key] = session;
            string json = JsonSerializer.Serialize(saved, Json);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, json); File.Move(temporary, path, overwrite: true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        if (historyChanged) GameHistoryChanged?.Invoke(server);
    }
    public static ForgemagingSession Copy(ForgemagingSession session) =>
        JsonSerializer.Deserialize<ForgemagingSession>(JsonSerializer.Serialize(session, Json), Json)!;
}
