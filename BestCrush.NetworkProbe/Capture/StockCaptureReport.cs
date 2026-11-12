using System.Globalization;
using System.Net;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace BestCrush.NetworkProbe.Capture;

internal sealed record StockReportSnapshot(long Id, string RunId, int Session,
    string Container, string CapturedUtc, string Status, int SourceRows);
internal sealed record StockReportItem(long SnapshotId, string Container, string ItemId,
    string Uid, string Quantity, string? EffectsJson, string? WireJson, string? EffectIssue);
internal sealed record StockReportCatalogEntry(string ItemId, string Name, int? Level, string Category, string Source);

internal sealed record StockCaptureReport(IReadOnlyList<StockReportSnapshot> Snapshots,
    IReadOnlyList<StockReportItem> Items)
{
    internal static SqliteConnection OpenReadOnly(string path)
    {
        SQLitePCL.Batteries_V2.Init();
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    internal static StockCaptureReport Read(string path, string? runId = null, int? session = null)
    {
        if ((runId is null) != (session is null))
            throw new ArgumentException("Specify both --run and --session, or neither.");
        using var connection = OpenReadOnly(path);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // Select the newest observed scope, never stitch different clients or runs together.
        if (runId is null)
        {
            command.CommandText = "SELECT run_id,session FROM stock_snapshots ORDER BY id DESC LIMIT 1";
            using var scope = command.ExecuteReader();
            if (!scope.Read()) throw new InvalidDataException("No stock snapshots have been captured.");
            runId = scope.GetString(0);
            session = scope.GetInt32(1);
        }
        command.CommandText = """
            SELECT id,run_id,session,container,captured_utc,status,source_rows FROM stock_snapshots
            WHERE run_id=$run AND session=$session AND id IN (
                SELECT max(id) FROM stock_snapshots WHERE run_id=$run AND session=$session GROUP BY container)
            ORDER BY container
            """;
        command.Parameters.AddWithValue("$run", runId);
        command.Parameters.AddWithValue("$session", session!);
        List<StockReportSnapshot> snapshots = [];
        using (var reader = command.ExecuteReader())
            while (reader.Read()) snapshots.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetInt32(6)));
        if (snapshots.Count == 0) throw new InvalidDataException("No snapshots for this scope.");
        if (snapshots.Any(x => x.Container is not ("bank" or "inventory")))
            throw new InvalidDataException("Invalid stock container.");
        List<StockReportItem> items = [];
        foreach (var snapshot in snapshots)
        {
            if (snapshot.Status != "CompleteCandidate") continue;
            using var rows = connection.CreateCommand();
            rows.Transaction = transaction;
            rows.CommandText = "SELECT item_id_candidate,uid_candidate,quantity_candidate,effects_json,effect_wire_json,effect_issue FROM stock_items WHERE snapshot_id=$id ORDER BY ordinal";
            rows.Parameters.AddWithValue("$id", snapshot.Id);
            using var reader = rows.ExecuteReader();
            int count = 0;
            while (reader.Read())
            {
                string quantity = reader.GetString(2);
                if (!ulong.TryParse(quantity, NumberStyles.None, CultureInfo.InvariantCulture, out ulong q) || q == 0)
                    throw new InvalidDataException("Invalid captured quantity.");
                items.Add(new(snapshot.Id, snapshot.Container, reader.GetString(0), reader.GetString(1), quantity,
                    reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
                count++;
            }
            if (count != snapshot.SourceRows) throw new InvalidDataException("Snapshot row count mismatch.");
        }
        transaction.Commit();
        return new(snapshots, items);
    }

    internal static Dictionary<string, StockReportCatalogEntry> ReadLocalCatalog(string? path)
    {
        Dictionary<string, StockReportCatalogEntry> entries = [];
        if (path is null) return entries;
        using var connection = OpenReadOnly(path);
        foreach (var (table, category) in new[] { ("Equipments", "Équipement"), ("Resources", "Ressource"), ("Runes", "Rune") })
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT DofusDbId,Name,Level FROM {table}";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string id = reader.GetInt64(0).ToString(CultureInfo.InvariantCulture);
                string name = reader.GetString(1);
                if (string.IsNullOrWhiteSpace(name) || name == "???") continue;
                entries[id] = new(id, name, reader.GetInt32(2), category, "Catalogue local BestCrush");
            }
        }
        return entries;
    }

    internal void Write(string outputDirectory, IReadOnlyDictionary<string, StockReportCatalogEntry> catalog)
    {
        Directory.CreateDirectory(outputDirectory);
        // JSON is local and retains row-level effect evidence. HTML avoids showing private UIDs/raw bytes.
        File.WriteAllText(Path.Combine(outputDirectory, "stock.json"), JsonSerializer.Serialize(new {
            GeneratedUtc = DateTimeOffset.UtcNow, Ownership = "unverified", Snapshots, Items,
            Catalog = Items.Select(x => x.ItemId).Distinct().Where(catalog.ContainsKey).Select(x => catalog[x])
        }, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        File.WriteAllText(Path.Combine(outputDirectory, "stock.html"), RenderHtml(catalog), Encoding.UTF8);
    }

    internal string RenderHtml(IReadOnlyDictionary<string, StockReportCatalogEntry> catalog)
    {
        static string H(string? value) => WebUtility.HtmlEncode(value ?? "");
        var html = new StringBuilder("""
            <!doctype html><html lang="fr"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>BestCrush · Banque et inventaire</title>
            <style>body{font:15px system-ui;background:#f5f6fa;color:#202639;margin:0}main{max-width:1400px;margin:auto;padding:32px}h1{margin:0 0 8px}p{line-height:1.5}.notice{background:#fff4d7;border-left:4px solid #d4a629;padding:12px 16px}.cards{display:flex;gap:16px;flex-wrap:wrap}.card{background:white;padding:18px 24px;border-radius:12px;min-width:220px}.card strong{display:block;font-size:28px}input,select{font:inherit;padding:10px;border:1px solid #cbd0dd;border-radius:7px}input{width:min(360px,80%)}.filters{display:flex;gap:12px;flex-wrap:wrap;margin:24px 0 12px}table{width:100%;border-collapse:collapse;background:white}td,th{text-align:left;padding:10px 12px;border-bottom:1px solid #e5e7ee}th{position:sticky;top:0;background:#e9edf5}td:nth-child(4){font-variant-numeric:tabular-nums;text-align:right}.muted{color:#697185}.missing{color:#945b00}details{margin:20px 0}summary{cursor:pointer}small{line-height:1.5}a{color:#2454a4}</style>
            <main><h1>Banque et inventaire</h1><p class="muted">BestCrush · relevé local des objets capturés</p>
            <p class="notice">Captures candidates datées. Compte et serveur non vérifiés automatiquement. Les mouvements après capture ne sont pas synchronisés. Les noms du catalogue ne valident pas les champs réseau.</p><div class="cards">
            """);
        foreach (string container in new[] { "bank", "inventory" })
        {
            var snapshot = Snapshots.FirstOrDefault(x => x.Container == container);
            var rows = Items.Where(x => x.Container == container).ToArray();
            string title = container == "bank" ? "Banque" : "Inventaire";
            string status = snapshot?.Status switch { "CompleteCandidate" => rows.Length + " lignes", "EmptyCandidate" => "Vide candidat", null => "Non capturé", _ => "Capture invalide" };
            BigInteger units = rows.Aggregate(BigInteger.Zero, (sum, row) => sum + BigInteger.Parse(row.Quantity, CultureInfo.InvariantCulture));
            html.Append($"<div class='card'>{title}<strong>{H(status)}</strong>{units:N0} unités candidates<br><small class='muted'>{H(snapshot?.CapturedUtc)}</small></div>");
        }
        int named = Items.Count(x => catalog.ContainsKey(x.ItemId));
        html.Append($"<div class='card'>Noms résolus<strong>{named} / {Items.Count}</strong>lignes · {Items.Count(x => x.WireJson is null)} sans effets bruts</div></div>");
        html.Append("""
            <div class="filters"><input id="search" type="search" aria-label="Rechercher un objet" placeholder="Rechercher un nom ou un identifiant…"><select id="container" aria-label="Conteneur"><option value="">Banque et inventaire</option value="bank">Banque</option><option value="inventory">Inventaire</option></select><select id="effects" aria-label="Décodage"><option value="">Tous les effets</option><option value="unknown">Effets à interpréter</option><option value="missing">Effets bruts manquants</option></select></div><p id="count" class="muted"></p>
            <table><thead><tr><th>Conteneur</th><th>Objet</th><th>ID candidat</th><th>Quantité</th><th>Catégorie</th><th>Niveau</th><th>Effets</th></tr></thead><tbody>
            """);
        foreach (var item in Items.OrderBy(x => catalog.GetValueOrDefault(x.ItemId)?.Name ?? x.ItemId, StringComparer.CurrentCultureIgnoreCase))
        {
            catalog.TryGetValue(item.ItemId, out var entry);
            string name = entry?.Name ?? "Objet non résolu";
            string effect = item.WireJson is null ? "Bruts manquants" : item.EffectsJson is null ? "Bruts conservés · à interpréter" : "Numériques décodés";
            string category = entry?.Category ?? "Inconnue";
            html.Append($"<tr data-container='{item.Container}' data-effects='{(item.WireJson is null ? "missing" : item.EffectsJson is null ? "unknown" : "decoded")}'><td>{(item.Container == "bank" ? "Banque" : "Inventaire")}</td><td>{H(name)}</td><td>{H(item.ItemId)}</td><td>{H(item.Quantity)}</td><td>{H(category)}</td><td>{entry?.Level}</td><td>{H(effect)}</td></tr>");
        }
        html.Append("""
            </tbody></table><details><summary>Provenance et lecture des données</summary><p>Une ligne correspond à une pile ou instance capturée. Les équipements aux effets différents restent sur des lignes distinctes. Les anciennes captures ne sont pas additionnées. Seul le dernier snapshot de chaque conteneur dans la session choisie est lu ; une capture invalide ne réutilise pas une ancienne capture complète.</p><p>Le fichier voisin <a href="stock.json">stock.json</a> conserve le détail et les références des snapshots pour traitement local. Les métadonnées proviennent du catalogue local BestCrush et, si demandé, du catalogue public DofusDb. Leur niveau correspond au catalogue, pas au niveau particulier d'un familier.</p></details></main>
            <script>const rows=[...document.querySelectorAll('tbody tr')],search=document.getElementById('search'),container=document.getElementById('container'),effects=document.getElementById('effects');function update(){let n=0;const q=search.value.toLocaleLowerCase('fr');for(const r of rows){const visible=(!container.value||r.dataset.container===container.value)&&(!effects.value||r.dataset.effects===effects.value)&&r.textContent.toLocaleLowerCase('fr').includes(q);r.hidden=!visible;if(visible)n++;}document.getElementById('count').textContent=n+' lignes affichées sur '+rows.length;}for(const control of [search,container,effects])control.addEventListener('input',update);update();</script></html>
            """);
        return html.ToString();
    }
}
