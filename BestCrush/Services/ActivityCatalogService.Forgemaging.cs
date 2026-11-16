using System.Net.Http.Json;
using System.Text.Json;
using BestCrush.Domain.Models;
using DofusSharp.DofusDb.ApiClients.Search;

namespace BestCrush.Services;

public sealed record ForgemagingEffectDefinition(ulong Code, Characteristic? Stat, int Sign);

public sealed partial class ActivityCatalogService
{
    private const string ForgemagingEffectsKey = "fm-effects-v1";

    internal IReadOnlyDictionary<ulong, ForgemagingEffectDefinition>? ReadCachedForgemagingEffects()
    {
        string key = CacheKey(ForgemagingEffectsKey);
        if (_memory.TryGetValue(key, out var known) && known is ForgemagingEffectDefinition[] effects)
            return effects.ToDictionary(effect => effect.Code);

        string file = CachePath(key);
        if (!File.Exists(file)) return null;
        try
        {
            var data = JsonSerializer.Deserialize<Cached<ForgemagingEffectDefinition[]>>(File.ReadAllText(file))?.Data;
            return data?.ToDictionary(effect => effect.Code);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return null; }
    }

    public Task<ForgemagingEffectDefinition[]> GetForgemagingEffectsAsync(CancellationToken token) =>
        CachedAsync(ForgemagingEffectsKey, async () =>
        {
            var characteristics = await Fetch(factory.Characteristics(), new() { Select = ["id", "keyword"] }, token);
            var stats = characteristics.Where(c => c.Id is not null && c.Keyword is not null).ToDictionary(
                c => c.Id!.Value, c => CharacteristicExtensions.CharacteristicFromDofusDbKeyword(c.Keyword!));
            List<ForgemagingEffectDefinition> result = [];
            using var client = http.CreateClient();
            for (int skip = 0; skip < 20000; skip += 50)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                var uri = new UriBuilder(Uri.UriSchemeHttps, Host) { Path = "/effects", Query =
                    $"$limit=50&$skip={skip}&$sort[id]=1&$select[]=id&$select[]=characteristic&$select[]=characteristicOperator&$select[]=category" }.Uri;
                using var page = await client.GetFromJsonAsync<JsonDocument>(uri, timeout.Token) ?? throw new InvalidDataException();
                foreach (var row in page.RootElement.GetProperty("data").EnumerateArray())
                {
                    ulong id = row.GetProperty("id").GetUInt64();
                    Characteristic? stat = row.TryGetProperty("category", out var cat) && cat.GetInt32() == 0 &&
                        row.TryGetProperty("characteristic", out var characteristic) && characteristic.TryGetInt64(out long c)
                        ? stats.GetValueOrDefault(c) : null;
                    string? op = row.TryGetProperty("characteristicOperator", out var sign) ? sign.GetString() : null;
                    result.Add(new(id, op is "+" or "-" ? stat : null, op == "-" ? -1 : 1));
                }
                if (skip + 50 >= page.RootElement.GetProperty("total").GetInt32()) return result.ToArray();
            }
            throw new InvalidDataException("Catalogue des effets trop volumineux.");
        }, token);
}
