using System.Collections.Concurrent;
using System.Text.Json;
using BestCrush.Domain.Models;
using DofusSharp.DofusDb.ApiClients;
using DofusSharp.DofusDb.ApiClients.Models;
using DofusSharp.DofusDb.ApiClients.Models.Items;
using DofusSharp.DofusDb.ApiClients.Models.Jobs;
using DofusSharp.DofusDb.ApiClients.Models.Maps;
using DofusSharp.DofusDb.ApiClients.Models.Monsters;
using DofusSharp.DofusDb.ApiClients.Search;

namespace BestCrush.Services;

public sealed record ActivityItem(long Id, string Name, int Level, long? TypeId,
    IReadOnlyList<long> DropMonsterIds, long? IconId = null);
public sealed record HarvestActivity(ActivityItem Item, string Job, int? RequiredLevel);
public sealed record EmptySoulStone(long Id, string Name, int Capacity);
public sealed record DungeonBoss(long Id, string Name, string Dungeon, int? Level,
    IReadOnlyList<EmptySoulStone> Stones);
public sealed record CombatSource(long Id, string Name, int? Level);
public sealed record RecipeProfession(long ItemId, string? Job, int? Level);

/// <summary>Public activity metadata, cached independently of prices and live characters.</summary>
public sealed partial class ActivityCatalogService(IDofusDbClientsFactory factory, IHttpClientFactory http, string cacheDirectory)
{
    private readonly ConcurrentDictionary<string, object> _memory = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private sealed record Cached<T>(DateTime UpdatedUtc, T Data);
    private string Host => factory.Items().GetGetRequestUri(1).Host;

    public Task<HarvestActivity[]> GetHarvestsAsync(CancellationToken token) => CachedAsync("harvests-v1", async () =>
    {
        var jobs = await Fetch(factory.Jobs(), new(), token);
        var skills = await Fetch(factory.Skills(), new() { Predicates =
            [new DofusDbSearchPredicate.GreaterThan("gatheredRessourceItem", "0")] }, token);
        return skills.Where(x => x.GatheredResourceItem is > 0 && x.Item?.Id is > 0)
            .Select(x => new HarvestActivity(ToItem(x.Item!),
                jobs.FirstOrDefault(j => j.Id == x.ParentJobId)?.Name?.Fr ?? $"Métier #{x.ParentJobId}", x.LevelMin))
            .DistinctBy(x => (x.Item.Id, x.Job)).ToArray();
    }, token);

    public Task<DungeonBoss[]> GetBossesAsync(CancellationToken token) => CachedAsync("dungeon-bosses-v2", async () =>
    {
        var dungeons = await Fetch(factory.Dungeons(), new() { Select = ["id", "name", "bosses", "optimalPlayerLevel"] }, token);
        long[] ids = dungeons.SelectMany(x => x.Bosses ?? []).Distinct().ToArray();
        List<DofusDbMonster> monsters = ids.Length == 0 ? [] : await Fetch(factory.Monsters(), new() {
            Predicates = [new DofusDbSearchPredicate.In("id", ids.Select(x => x.ToString()).ToArray())],
            Select = ["id", "name", "grades", "isBoss", "isMiniBoss", "soulCaptureForbidden"] }, token);
        var empty = await LoadEmptySoulStonesAsync(token);
        return monsters.Where(x => x.Id is > 0 && x.IsBoss == true && x.IsMiniBoss != true && x.SoulCaptureForbidden == false)
            .Select(x => {
                int? level = x.Grades?.Where(g => g.Level is not null).Select(g => g.Level).DefaultIfEmpty().Max();
                return new DungeonBoss(x.Id!.Value, x.Name?.Fr ?? $"Boss #{x.Id}",
                    dungeons.FirstOrDefault(d => d.Bosses?.Contains(x.Id.Value) == true)?.Name?.Fr ?? "Donjon",
                    level, empty.Where(s => level is not null && s.Capacity >= level).OrderBy(s => s.Capacity).ToArray());
            }).OrderBy(x => x.Level).ThenBy(x => x.Name).ToArray();
    }, token);

    public async Task<ActivityItem[]> GetItemsAsync(IReadOnlyCollection<long> ids, CancellationToken token)
    {
        List<ActivityItem> result = [];
        foreach (var batch in ids.Distinct().Order().Chunk(100))
        {
            string key = "items-v1-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(string.Join(',', batch))))[..20];
            result.AddRange(await CachedAsync(key, async () => (await Fetch(factory.Items(), new() {
                Predicates = [new DofusDbSearchPredicate.In("id", batch.Select(x => x.ToString()).ToArray())],
                Select = ["id", "name", "level", "typeId", "dropMonsterIds", "iconId"] }, token))
                .Where(x => x.Id is > 0).Select(ToItem).ToArray(), token));
        }
        return result.ToArray();
    }

    public async Task<IReadOnlyDictionary<long, long>> GetItemIconIdsAsync(IReadOnlyCollection<long> ids, CancellationToken token)
    {
        var result = new Dictionary<long, long>();
        foreach (var batch in ids.Distinct().Order().Chunk(100))
        {
            // Older item and harvest caches can have complete activity data but no artwork IDs.
            var icons = await CachedAsync("item-icons-v1-" + string.Join('-', batch), async () =>
            {
                var items = await Fetch(factory.Items(), new()
                {
                    Predicates = [new DofusDbSearchPredicate.In("id", batch.Select(id => id.ToString()).ToArray())],
                    Select = ["id", "iconId"]
                }, token);
                return items.Where(item => item.Id is > 0 && item.IconId is > 0 && batch.Contains(item.Id.Value))
                    .DistinctBy(item => item.Id).ToDictionary(item => item.Id!.Value, item => item.IconId!.Value);
            }, token);
            foreach (var (id, iconId) in icons)
                result[id] = iconId;
        }
        return result;
    }

    public Task<CombatSource[]> GetCombatSourcesAsync(IReadOnlyCollection<long> monsterIds, CancellationToken token)
    {
        long[] ids = monsterIds.Distinct().Take(50).Order().ToArray();
        return ids.Length == 0 ? Task.FromResult(Array.Empty<CombatSource>()) : GetCombatSourceBatchAsync(ids, token);
    }

    public async Task<CombatSource[]> GetFarmingCombatSourcesAsync(IReadOnlyCollection<long> monsterIds, CancellationToken token)
    {
        List<CombatSource> sources = [];
        foreach (var batch in monsterIds.Distinct().Order().Chunk(50))
        {
            token.ThrowIfCancellationRequested();
            sources.AddRange(await GetCombatSourceBatchAsync(batch, token));
        }
        return sources.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Id).ToArray();
    }

    private Task<CombatSource[]> GetCombatSourceBatchAsync(long[] ids, CancellationToken token)
    {
        return CachedAsync("monsters-v1-" + string.Join('-', ids), async () =>
            (await Fetch(factory.Monsters(), new() { Predicates =
                [new DofusDbSearchPredicate.In("id", ids.Select(x => x.ToString()).ToArray())],
                Select = ["id", "name", "grades"] }, token)).Where(x => x.Id is > 0)
            .Select(x => new CombatSource(x.Id!.Value, x.Name?.Fr ?? $"Monstre #{x.Id}",
                x.Grades?.Where(g => g.Level is not null).Select(g => g.Level).DefaultIfEmpty().Max())).ToArray(), token);
    }

    public async Task<IReadOnlyDictionary<long, RecipeProfession>> GetRecipeProfessionsAsync(
        IReadOnlyCollection<long> itemIds, CancellationToken token)
    {
        if (itemIds.Count == 0) return new Dictionary<long, RecipeProfession>();
        var jobs = await CachedAsync("jobs-v1", async () => (await Fetch(factory.Jobs(), new(), token)).ToArray(), token);
        var result = new Dictionary<long, RecipeProfession>();
        foreach (var batch in itemIds.Distinct().Order().Chunk(100))
        {
            string key = "recipe-professions-v1-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(string.Join(',', batch))))[..20];
            var professions = await CachedAsync(key, async () =>
            {
                var recipes = await Fetch(factory.Recipes(), new() { Predicates =
                    [new DofusDbSearchPredicate.In("resultId", batch.Select(x => x.ToString()).ToArray())],
                    Select = ["resultId", "jobId", "resultLevel"] }, token);
                return recipes.Where(x => x.ResultId is > 0).Select(recipe => new RecipeProfession(recipe.ResultId!.Value,
                    jobs.FirstOrDefault(x => x.Id == recipe.JobId)?.Name?.Fr, recipe.ResultLevel)).ToArray();
            }, token);
            foreach (var profession in professions) result[profession.ItemId] = profession;
        }
        return result;
    }

    private async Task<T> CachedAsync<T>(string key, Func<Task<T>> load, CancellationToken token) where T : class
    {
        key = Host.Replace('.', '-') + "-" + key;
        if (_memory.TryGetValue(key, out var known)) return (T)known;
        await _gate.WaitAsync(token);
        try
        {
            if (_memory.TryGetValue(key, out known)) return (T)known;
            Directory.CreateDirectory(cacheDirectory);
            // Hash long monster lists before using them as filenames.
            string file = Path.Combine(cacheDirectory, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(key))) + ".json");
            Cached<T>? cached = null;
            if (File.Exists(file))
            {
                try { cached = JsonSerializer.Deserialize<Cached<T>>(await File.ReadAllTextAsync(file, token)); }
                catch (JsonException) { }
            }
            T data;
            if (cached is not null && DateTime.UtcNow - cached.UpdatedUtc < TimeSpan.FromDays(7)) data = cached.Data;
            else
            {
                bool loadedFresh = true;
                try { data = await load(); }
                catch (Exception) when (cached is not null && !token.IsCancellationRequested) { data = cached.Data; loadedFresh = false; }
                token.ThrowIfCancellationRequested();
                if (loadedFresh)
                {
                    string temporary = file + ".tmp";
                    await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new Cached<T>(DateTime.UtcNow, data)), token);
                    File.Move(temporary, file, true);
                }
            }
            _memory[key] = data;
            return data;
        }
        finally { _gate.Release(); }
    }

    private async Task<List<T>> Fetch<T>(IDofusDbTableClient<T> client, DofusDbSearchQuery query, CancellationToken token)
        where T : DofusDbResource
    {
        client.HttpClientFactory = http;
        List<T> result = [];
        while (true)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var page = await client.SearchAsync(new() { Limit = 50, Skip = result.Count,
                Select = query.Select, Predicates = query.Predicates, Sort = new Dictionary<string, DofusDbSearchQuerySortOrder>
                    { ["id"] = DofusDbSearchQuerySortOrder.Ascending } }, timeout.Token);
            if (page.Data.Count == 0) break;
            result.AddRange(page.Data);
            if (result.Count >= page.Total) break;
            if (result.Count > 20000) throw new InvalidDataException("Catalogue trop volumineux.");
        }
        return result;
    }
    private static ActivityItem ToItem(DofusDbItem item) => new(item.Id!.Value,
        item.Name?.Fr ?? item.Name?.En ?? $"Objet #{item.Id}", item.Level ?? 0, item.TypeId, (item.DropMonsterIds ?? []).ToArray(), item.IconId);
}
