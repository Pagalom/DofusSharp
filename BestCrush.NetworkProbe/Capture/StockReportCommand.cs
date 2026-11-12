using System.Globalization;
using System.Text.Json;
using DofusSharp.DofusDb.ApiClients;
using DofusSharp.DofusDb.ApiClients.Search;

namespace BestCrush.NetworkProbe.Capture;

internal static class StockReportCommand
{
    internal static async Task RunAsync(string[] args)
    {
        string? db = null, catalogPath = null, output = null, run = null;
        int? session = null;
        bool resolve = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--stock-report": db = Value(); break;
                case "--catalog-db": catalogPath = Value(); break;
                case "--output-dir": output = Value(); break;
                case "--run": run = Value(); break;
                case "--session": session = int.Parse(Value(), CultureInfo.InvariantCulture); break;
                case "--resolve-catalog": resolve = true; break;
                default: throw new ArgumentException("Unknown stock report argument: " + args[i]);
            }
            string Value() => ++i < args.Length && !args[i].StartsWith("--", StringComparison.Ordinal)
                ? args[i] : throw new ArgumentException("Missing argument value.");
        }
        if (db is null || output is null) throw new ArgumentException("Use --stock-report database.db --output-dir directory [--catalog-db bestcrush.db] [--resolve-catalog] [--run id --session n].");
        var report = StockCaptureReport.Read(db, run, session);
        var catalog = StockCaptureReport.ReadLocalCatalog(catalogPath);
        Directory.CreateDirectory(output);
        string cachePath = Path.Combine(output, "catalog-cache.json");
        if (File.Exists(cachePath))
        {
            var cached = JsonSerializer.Deserialize<StockReportCatalogEntry[]>(File.ReadAllText(cachePath)) ?? [];
            foreach (var entry in cached)
                if (!catalog.ContainsKey(entry.ItemId)) catalog[entry.ItemId] = entry;
        }
        if (resolve)
        {
            string[] missing = report.Items.Select(x => x.ItemId).Distinct().Where(x => !catalog.ContainsKey(x)).ToArray();
            var client = DofusDbClient.Production().Items();
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            // Public item IDs only; no UIDs, quantities, character/account identifiers or effects are sent.
            foreach (string[] batch in missing.Chunk(40))
            {
                try
                {
                    int skip = 0;
                    while (true)
                    {
                        var page = await client.SearchAsync(new DofusDbSearchQuery {
                            Limit = 40, Skip = skip,
                            Select = ["id", "name", "level", "type"],
                            Predicates = [new DofusDbSearchPredicate.In("id", batch)]
                        }, deadline.Token);
                        foreach (var item in page.Data)
                        {
                            string? id = item.Id?.ToString(CultureInfo.InvariantCulture);
                            if (id is null || !batch.Contains(id) || string.IsNullOrWhiteSpace(item.Name?.Fr)) continue;
                            catalog[id] = new(id, item.Name.Fr, item.Level,
                                item.Type?.Name?.Fr ?? "Catalogue public", "DofusDb · " + DateTimeOffset.UtcNow.ToString("O"));
                        }
                        skip += page.Data.Count;
                        if (page.Data.Count == 0 || skip >= Math.Min(page.Total, batch.Length)) break;
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
                {
                    Console.Error.WriteLine("Catalogue public partiellement indisponible : " + ex.GetType().Name);
                    break;
                }
            }
            File.WriteAllText(cachePath, JsonSerializer.Serialize(catalog.Values.ToArray(), new JsonSerializerOptions { WriteIndented = true }));
        }
        report.Write(output, catalog);
        Console.WriteLine($"Rapport créé : {Path.GetFullPath(Path.Combine(output, "stock.html"))}");
        Console.WriteLine($"Lignes : {report.Items.Count}; noms résolus : {report.Items.Count(x => catalog.ContainsKey(x.ItemId))}; effets bruts manquants : {report.Items.Count(x => x.WireJson is null)}.");
    }
}
