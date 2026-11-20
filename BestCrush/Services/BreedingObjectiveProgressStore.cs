using System.Text.Json;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;

namespace BestCrush.Services;

// User planning only. No sale, stock, character identity or profit is written to the economic ledger.
public sealed class BreedingObjectiveProgressStore(string path)
{
    private readonly object _sync = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string Key(BreedingMarketObjective objective) => $"{objective.ItemId}:{objective.Sale.Level}:{objective.Sale.Fertile}";

    public BreedingObjectiveJournal Read(string scope)
    {
        lock (_sync) return Load().GetValueOrDefault(scope) ?? new();
    }

    public void Save(string scope, BreedingObjectiveJournal journal)
    {
        lock (_sync)
        {
            var all = Load(); // Never overwrite a damaged journal.
            all[scope] = journal;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, JsonSerializer.Serialize(all, Json)); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    private Dictionary<string, BreedingObjectiveJournal> Load()
    {
        if (!File.Exists(path)) return [];
        try
        {
            var all = JsonSerializer.Deserialize<Dictionary<string, BreedingObjectiveJournal>>(File.ReadAllText(path), Json)
                ?? throw new JsonException();
            if (all.Values.Any(j => j is null || j.Goals is null || j.Pending is null || j.ObservedListings is null || j.ObservedSales is null ||
                j.Goals.Values.Any(g => g is null || g.Steps is null || g.Done is null || g.Partial is null || g.PendingListings is null || g.Done.Any(i => i < 0 || i >= g.Steps.Count))))
                throw new JsonException();
            return all;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        { throw new InvalidDataException("Suivi des objectifs illisible. Le fichier est conservé.", ex); }
    }

    public static bool Observe(BreedingObjectiveProgress progress, IReadOnlyList<PaddockMountReading> mounts)
    {
        bool changed = false;
        var acquired = progress.Steps.Where(s => s.Action is BreedingObjectiveAction.Capture or BreedingObjectiveAction.Buy or BreedingObjectiveAction.Cross)
            .Select(s => s.OwnedId).OfType<Guid>().ToHashSet();
        for (int index = 0; index < progress.Steps.Count; index++)
        {
            if (progress.Done.Contains(index)) continue;
            var step = progress.Steps[index];
            if (step.Action == BreedingObjectiveAction.Sell) continue;
            var candidates = mounts.Where(m => step.RideId is not null && m.SpeciesId == step.RideId &&
                (step.Sex is null || m.Sex == step.Sex)).ToArray();
            if (step.Action is BreedingObjectiveAction.Capture or BreedingObjectiveAction.Buy && candidates.Length == 0 && step.Sex is not null &&
                mounts.Any(m => m.SpeciesId == step.RideId && m.Sex is null && m.Fertility == BreedingMountFertility.Fertile) &&
                progress.Partial.GetValueOrDefault(index) < .5)
            { progress.Partial[index] = .5; changed = true; }
            bool acquisition = step.Action is BreedingObjectiveAction.Capture or BreedingObjectiveAction.Buy or BreedingObjectiveAction.Cross;
            if (acquisition) candidates = candidates.Where(m => m.Id == step.OwnedId || !acquired.Contains(m.Id)).ToArray();
            var mount = candidates.FirstOrDefault(m => m.Id == step.OwnedId) ?? (candidates.Length == 1 ? candidates[0] : null);
            if (acquisition && mount is null && candidates.Length > 1)
                mount = candidates.OrderBy(m => m.Id).FirstOrDefault(m => step.Sex is null ||
                    m.Fertility is BreedingMountFertility.Fertile or BreedingMountFertility.Ready);
            // A target already obtained satisfies the outcome; this never asserts which parents produced it.
            if (mount is not null && acquisition && (step.Sex is null ||
                mount.Fertility is BreedingMountFertility.Fertile or BreedingMountFertility.Ready))
            {
                acquired.Add(mount.Id);
                progress.Steps[index] = step = step with { OwnedId = mount.Id };
                // Link only this acquisition's following preparation/training, before another acquisition of that species.
                for (int next = index + 1; next < progress.Steps.Count; next++)
                {
                    var follow = progress.Steps[next];
                    if (follow.RideId != step.RideId || follow.Sex != step.Sex) continue;
                    if (follow.Action is BreedingObjectiveAction.Capture or BreedingObjectiveAction.Buy or BreedingObjectiveAction.Cross) break;
                    if (follow.OwnedId is null) progress.Steps[next] = follow with { OwnedId = mount.Id };
                }
                changed = true;
            }
            if (mount is not null && step.Action is BreedingObjectiveAction.Prepare or BreedingObjectiveAction.Train && step.OwnedId != mount.Id)
            { progress.Steps[index] = step = step with { OwnedId = mount.Id }; changed = true; }
            bool complete = step.Action switch
            {
                BreedingObjectiveAction.Capture or BreedingObjectiveAction.Buy or BreedingObjectiveAction.Cross =>
                    mount is not null && (step.Sex is null || mount.Fertility is BreedingMountFertility.Fertile or BreedingMountFertility.Ready),
                BreedingObjectiveAction.Prepare => mount is { Fertility: BreedingMountFertility.Ready } && mount.Level >= (step.Level ?? 1),
                BreedingObjectiveAction.Train => mount is not null && mount.Level >= (step.Level ?? 1),
                _ => false
            };
            double partial = 0;
            if (mount is not null && step.Action is BreedingObjectiveAction.Train or BreedingObjectiveAction.Prepare)
            {
                int targetXp = BreedingMountExperience.RequiredAtLevel(step.Level ?? 1);
                double xp = targetXp == 0 ? 1 : Math.Clamp((double)mount.Experience / targetXp, 0, 1);
                partial = step.Action == BreedingObjectiveAction.Train ? xp :
                    (Math.Clamp((mount.Endurance ?? 0) / 20000d, 0, 1) + Math.Clamp((mount.Maturity ?? 0) / 20000d, 0, 1) +
                     Math.Clamp((mount.Love ?? 0) / 20000d, 0, 1) + (targetXp > 0 ? xp : 0)) / (targetXp > 0 ? 4 : 3);
            }
            if (complete) { progress.Done.Add(index); changed = true; }
            else if (partial > progress.Partial.GetValueOrDefault(index))
            { progress.Partial[index] = Math.Min(.99, partial); changed = true; }
        }
        return changed;
    }

    public static int Percent(BreedingObjectiveProgress progress) => progress.PendingListings.Count > 0 ? 95 : progress.Steps.Count == 0 ? 0 :
        (int)(100 * Enumerable.Range(0, progress.Steps.Count).Sum(i => progress.Done.Contains(i) ? 1d :
            Math.Clamp(progress.Partial.GetValueOrDefault(i), 0, .99)) / progress.Steps.Count);

    internal static bool ObserveListings(BreedingObjectiveJournal journal, IReadOnlyList<BreedingMarketObjective> objectives,
        IReadOnlyDictionary<string, IReadOnlyList<BreedingObjectiveStep>> plans, IReadOnlyList<ProductionListing> listings,
        DateTimeOffset now, IReadOnlyList<ProductionSale>? sales = null, IReadOnlyList<ProductionReturn>? returns = null)
    {
        bool changed = false;
        foreach (var listing in listings.Where(l => !journal.ObservedListings.Contains(l.Id)))
        {
            var match = journal.Goals.FirstOrDefault(pair =>
            {
                var identity = pair.Key.Split(':');
                return identity.Length == 3 && long.TryParse(identity[0], out long item) && item == listing.ItemId &&
                    int.TryParse(identity[1], out int level) && level == listing.Level &&
                    bool.TryParse(identity[2], out bool fertile) && fertile == listing.Fertile &&
                    listing.At >= pair.Value.StartedAt && pair.Value.Steps.Any(s => s.Action == BreedingObjectiveAction.Sell);
            });
            if (match.Value is not { } progress) continue;
            journal.ObservedListings.Add(listing.Id);
            // A confirmed deposit with the target characteristics closes production, never an encashed sale.
            progress.PendingListings[listing.Id] = listing.At;
            progress.LastListedAt = listing.At;
            changed = true;
        }
        foreach (var (key, progress) in journal.Goals)
        {
            foreach (var returned in returns ?? [])
            {
                if (!progress.PendingListings.TryGetValue(returned.ListingId, out var listedAt) || returned.At < listedAt ||
                    !journal.ObservedReturns.Add(returned.Id)) continue;
                progress.PendingListings.Remove(returned.ListingId);
                if (progress.PendingListings.Count == 0)
                {
                    progress.Steps = progress.Steps.Where(s => s.Action == BreedingObjectiveAction.Sell).ToList();
                    progress.Done.Clear(); progress.Partial.Clear(); progress.ReturnedAt = returned.At;
                }
                changed = true;
            }
            foreach (var sale in sales ?? [])
            {
                if (!progress.PendingListings.TryGetValue(sale.ListingId, out var listedAt) || sale.At < listedAt ||
                    !journal.ObservedSales.Add(sale.Id)) continue;
                progress.PendingListings.Remove(sale.ListingId); progress.Cycles++; changed = true;
                if (progress.PendingListings.Count > 0) continue;
                progress.Done.Clear(); progress.Partial.Clear(); progress.OverrideCurrentCycle = false;
                progress.Steps = plans.GetValueOrDefault(key, progress.Steps).Select(s => s with { OwnedId = null }).ToList();
                progress.StartedAt = sale.At.AddTicks(1);
            }
        }
        return changed;
    }

    public static DateTimeOffset? WaitingUntil(BreedingObjectiveJournal journal, long itemId, DateTimeOffset now) =>
        journal.Pending.Where(p => p.ItemId == itemId && p.ReviewAfter > now).Select(p => (DateTimeOffset?)p.ReviewAfter).Max();

    public static bool Available(BreedingObjectiveJournal journal, BreedingMarketObjective goal, DateTimeOffset now) =>
        goal.Margin > 0 && goal.Capacity is { Evidence: BreedingDemandEvidence.Observed } capacity && now - capacity.ObservedAtUtc <= BreedingMarketDemand.Lifetime &&
        (journal.Goals.GetValueOrDefault(Key(goal))?.OverrideCurrentCycle == true ||
         goal.CanSell && WaitingUntil(journal, goal.ItemId, now) is null);

    public static void ConfirmStep(BreedingObjectiveJournal journal, BreedingMarketObjective goal,
        IReadOnlyList<BreedingObjectiveStep> nextPlan, int index, DateTimeOffset now)
    {
        var progress = journal.Goals[Key(goal)];
        int next = Enumerable.Range(0, progress.Steps.Count).FirstOrDefault(i => !progress.Done.Contains(i), -1);
        if (index != next || index < 0) throw new InvalidOperationException("Étape déjà traitée ou hors parcours.");
        if (progress.Steps[index].Action != BreedingObjectiveAction.Sell) { progress.Done.Add(index); return; }
        if (!Available(journal, goal, now)) throw new InvalidOperationException("Attendre un débouché avant une nouvelle mise en vente.");
        // A manual listing is not a completed sale. Hold production for an estimated absorption interval,
        // shared by every target level of this species. The live daily/weekly quota remains authoritative.
        double days = Math.Max(1d / 24, 7d / Math.Max(1, goal.Capacity!.SalesAtPrice));
        var previous = WaitingUntil(journal, goal.ItemId, now) ?? now;
        journal.Pending.Add(new(goal.ItemId, now, previous.AddDays(days)));
        progress.Cycles++;
        progress.OverrideCurrentCycle = false;
        progress.Done.Clear();
        progress.Partial.Clear();
        progress.Steps = nextPlan.ToList();
        progress.LastListedAt = now;
    }
}

public sealed class BreedingObjectiveJournal
{
    public string? Selected { get; set; }
    public Dictionary<string, BreedingObjectiveProgress> Goals { get; set; } = [];
    public List<BreedingPendingProduction> Pending { get; set; } = [];
    public HashSet<string> ObservedListings { get; set; } = [];
    public HashSet<string> ObservedSales { get; set; } = [];
    public HashSet<string> ObservedReturns { get; set; } = [];
}
public sealed class BreedingObjectiveProgress
{
    public BreedingPlanningOptions? Planning { get; set; }
    public List<BreedingObjectiveStep> Steps { get; set; } = [];
    public HashSet<int> Done { get; set; } = [];
    public Dictionary<int, double> Partial { get; set; } = [];
    public DateTimeOffset StartedAt { get; set; }
    public int Cycles { get; set; }
    public bool OverrideCurrentCycle { get; set; }
    public DateTimeOffset? LastListedAt { get; set; }
    public DateTimeOffset? ReturnedAt { get; set; }
    public Dictionary<string, DateTimeOffset> PendingListings { get; set; } = [];
}
public sealed record BreedingPendingProduction(long ItemId, DateTimeOffset ListedAt, DateTimeOffset ReviewAfter);
