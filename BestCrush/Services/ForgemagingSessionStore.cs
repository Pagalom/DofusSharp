using System.Text.Json;
using BestCrush.Domain.Models;
using BestCrush.Network.Protocol;

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
        public Dictionary<string, string> InstanceAliases { get; set; } = [];
        public Dictionary<string, EquipmentAcquisition> Acquisitions { get; set; } = [];
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
        lock (_sync)
        {
            var saved = Read();
            return FindObserved(saved, Key(server, itemId, key));
        }
    }

    private static ForgemagingSession? FindObserved(SavedSessions saved, string key) =>
        saved.Sessions.GetValueOrDefault(saved.InstanceAliases.GetValueOrDefault(key, key));

    internal Dictionary<ulong, long> FindObservedItemIds(string server, IEnumerable<ulong> uids)
    {
        lock (_sync)
        {
            var saved = Read();
            string prefix = server.ToUpperInvariant() + ":";
            var itemIds = saved.Sessions.Where(entry => entry.Key.StartsWith(prefix, StringComparison.Ordinal) &&
                    entry.Value.GameRecordingId is not null)
                .Select(entry => entry.Value.ItemId).Distinct().ToArray();
            var result = new Dictionary<ulong, long>();
            foreach (ulong uid in uids.Distinct())
            {
                var matches = itemIds.Where(id => FindObserved(saved, Key(server, id,
                    ForgemagedEquipmentStock.InstanceKey(server, id, uid))) is not null).Take(2).ToArray();
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
                if (FindObserved(saved, Key(server, item.ItemId, item.Key)) is { } session)
                    result[item.Key] = session;
            return result;
        }
    }

    internal Dictionary<string, EquipmentAcquisition> GetAcquisitions(string server, IEnumerable<(long ItemId, string Key)> items)
    {
        lock (_sync)
        {
            var saved = Read();
            var result = new Dictionary<string, EquipmentAcquisition>();
            foreach (var item in items)
            {
                var key = Key(server, item.ItemId, item.Key);
                if (saved.Acquisitions.TryGetValue(saved.InstanceAliases.GetValueOrDefault(key, key), out var acquisition))
                    result[item.Key] = acquisition;
            }
            return result;
        }
    }

    internal void RecordAcquisition(string server, long itemId, string recording, EquipmentAcquisition acquisition)
    {
        if (string.IsNullOrWhiteSpace(server) || itemId <= 0 || string.IsNullOrWhiteSpace(recording) ||
            (acquisition.Kind == EquipmentAcquisitionKind.Purchase ? acquisition.PricePaid is not >= 0
                : acquisition.Kind != EquipmentAcquisitionKind.Craft || acquisition.PricePaid is not null))
            throw new ArgumentException("Origine de l’équipement invalide.", nameof(acquisition));

        lock (_sync)
        {
            var saved = Read();
            string key = Key(server, itemId, recording);
            key = saved.InstanceAliases.GetValueOrDefault(key, key);
            if (saved.Acquisitions.TryGetValue(key, out var previous) &&
                (!acquisition.Manual || (previous.Manual && previous.RecordedAtUtc > acquisition.RecordedAtUtc)))
                return;

            saved.Acquisitions[key] = acquisition;
            Write(saved);
        }
        GameHistoryChanged?.Invoke(server);
    }

    // Effects are compared byte for byte as a multiset, including variants that lack a numeric value.
    private static string? ItemSignature(LiveStockItem item) =>
        item.ItemId is > 0 and <= long.MaxValue && item.Quantity == 1 && item.EffectWire is { Length: > 0 } wire
            ? $"{item.ItemId}:{item.Quantity}:" + string.Join('.', wire.Select(Convert.ToBase64String).Order(StringComparer.Ordinal))
            : null;

    internal bool ReattachObserved(string server, LiveStockContainer previous, LiveStockContainer current,
        IReadOnlyDictionary<ulong, ForgemagingEffectDefinition>? effects = null)
    {
        if (!previous.Complete || previous.Stale || !current.Complete || current.Stale ||
            previous.Container != current.Container || previous.UpdatedUtc >= current.UpdatedUtc)
            return false;
        static ILookup<string, LiveStockItem> Groups(LiveStockContainer container, Func<LiveStockItem, string?> signature) =>
            container.Items.Select(item => (Item: item, Signature: signature(item)))
                .Where(row => row.Signature is not null).ToLookup(row => row.Signature!, row => row.Item, StringComparer.Ordinal);
        string ItemKey(LiveStockItem item) =>
            Key(server, (long)item.ItemId, ForgemagedEquipmentStock.InstanceKey(server, (long)item.ItemId, item.Uid));
        lock (_sync)
        {
            var saved = Read();

            // An inventory snapshot can still contain the jet from before the last
            // workshop result. Use the newer, complete workshop jet for those objects.
            var updatedRolls = new Dictionary<ulong, string?>();
            var comparableItems = new Dictionary<LiveStockItem, string>();
            if (effects is not null)
            {
                foreach (var item in previous.Items.Where(item => item.ItemId is > 0 and <= long.MaxValue && item.Quantity == 1))
                {
                    var session = FindObserved(saved, ItemKey(item));
                    if (session?.LastObservedRollAtUtc is not { } observed || observed <= previous.UpdatedUtc ||
                        observed >= current.UpdatedUtc || !session.Attempts.Any(attempt => attempt.FromGame) ||
                        session.Lines.Count == 0 || session.Lines.Any(line => line.Current is null))
                        continue;

                    // For a changed jet, compare complete numeric effects from both
                    // snapshots only. If the effects are opaque, do not reuse
                    // the old inventory jet for this known workshop change.
                    var copies = previous.Items.Concat(current.Items)
                        .Where(copy => copy.ItemId == item.ItemId && copy.Quantity == 1).ToArray();
                    var signatures = copies.Select(copy => (Item: copy, Value: StockRollSignature(copy, effects))).ToArray();
                    if (signatures.Any(signature => signature.Value is null))
                    {
                        updatedRolls[item.Uid] = null;
                        continue;
                    }

                    foreach (var (copy, value) in signatures) comparableItems[copy] = value!;
                    updatedRolls[item.Uid] = NumericRollSignature(item.ItemId,
                        session.Lines.Select(line => (line.Characteristic, line.Current!.Value)));
                }
            }

            string? StockSignature(LiveStockItem item) =>
                comparableItems.GetValueOrDefault(item) ?? ItemSignature(item);
            var before = Groups(previous, item => updatedRolls.TryGetValue(item.Uid, out var roll) ? roll : StockSignature(item));
            var after = Groups(current, StockSignature);

            bool HasHistory(string key) => saved.Acquisitions.ContainsKey(key) ||
                (saved.Sessions.TryGetValue(key, out var session) && session.Attempts.Any(attempt => attempt.FromGame));
            var occupied = current.Items.Where(item => item.ItemId is > 0 and <= long.MaxValue)
                .Select(ItemKey).Select(key => saved.InstanceAliases.GetValueOrDefault(key, key)).ToHashSet(StringComparer.Ordinal);
            bool changed = false;
            foreach (var group in before)
            {
                var next = after[group.Key].OrderBy(item => item.Uid).ToArray();
                if (next.Length == 0 || next.Length != group.Count()) continue;
                var stable = group.Select(item => item.Uid).Intersect(next.Select(item => item.Uid)).ToHashSet();
                var previousItems = group.Where(item => !stable.Contains(item.Uid)).OrderBy(item => item.Uid);
                var currentItems = next.Where(item => !stable.Contains(item.Uid) &&
                    !saved.InstanceAliases.ContainsKey(ItemKey(item)) &&
                    !HasHistory(ItemKey(item)) && FindObserved(saved, ItemKey(item)) is null).ToArray();
                var availableSessions = previousItems.Select(ItemKey)
                    .Select(key => saved.InstanceAliases.GetValueOrDefault(key, key))
                    .Distinct(StringComparer.Ordinal)
                    .Where(key => !occupied.Contains(key) && HasHistory(key)).ToArray();
                foreach (var (destination, recording) in currentItems.Zip(availableSessions))
                {
                    saved.InstanceAliases[ItemKey(destination)] = recording;
                    occupied.Add(recording);
                    changed = true;
                }
            }
            if (changed) Write(saved);
            return changed;
        }
    }

    private static string? StockRollSignature(LiveStockItem item, IReadOnlyDictionary<ulong, ForgemagingEffectDefinition> definitions)
    {
        if (item.EffectIssue is not null || item.Effects is not { Count: > 0 } effects ||
            effects.Any(effect => !definitions.TryGetValue(effect.EffectCode, out var definition) || definition.Stat is null) ||
            ForgemagedEquipmentStock.MapEffects(effects.Select(effect => (effect.EffectCode, (long?)effect.Value)), definitions) is not { } stats)
            return null;

        return NumericRollSignature(item.ItemId, stats.Select(stat => (stat.Key, stat.Value)));
    }

    private static string NumericRollSignature(ulong itemId, IEnumerable<(Characteristic Stat, int Amount)> effects) =>
        $"fm:{itemId}:" + string.Join(',', effects.Where(effect => effect.Amount != 0).OrderBy(effect => effect.Stat)
            .Select(effect => FormattableString.Invariant($"{(int)effect.Stat}={effect.Amount}")));

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
                if (saved.InstanceAliases.TryGetValue(key, out var canonical))
                {
                    key = canonical;
                    session.GameRecordingId = key[(key.LastIndexOf(':') + 1)..];
                }
                var previous = saved.Sessions.GetValueOrDefault(key);
                historyChanged = previous?.Attempts.Count != session.Attempts.Count ||
                    previous?.LastObservedRollAtUtc != session.LastObservedRollAtUtc;
            }
            saved.Sessions[key] = session;
            Write(saved);
        }
        if (historyChanged) GameHistoryChanged?.Invoke(server);
    }

    private void Write(SavedSessions saved)
    {
        string json = JsonSerializer.Serialize(saved, Json);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, json); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static ForgemagingSession Copy(ForgemagingSession session) =>
        JsonSerializer.Deserialize<ForgemagingSession>(JsonSerializer.Serialize(session, Json), Json)!;
}
