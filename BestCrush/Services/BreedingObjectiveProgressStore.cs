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
            if (all.Values.Any(j => j is null || j.Goals is null || j.Pending is null ||
                j.Goals.Values.Any(g => g is null || g.Steps is null || g.Done is null || g.Done.Any(i => i < 0 || i >= g.Steps.Count))))
                throw new JsonException();
            return all;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        { throw new InvalidDataException("Suivi des objectifs illisible. Le fichier est conservé.", ex); }
    }

    public static bool Observe(BreedingObjectiveProgress progress, IReadOnlyList<PaddockMountReading> mounts)
    {
        bool changed = false;
        for (int index = 0; index < progress.Steps.Count; index++)
        {
            if (progress.Done.Contains(index)) continue;
            var step = progress.Steps[index];
            // A cross and a listing cannot be proved by a snapshot alone.
            if (step.Action is BreedingObjectiveAction.Cross or BreedingObjectiveAction.Sell) break;
            var candidates = mounts.Where(m => step.RideId is not null && m.SpeciesId == step.RideId &&
                (step.Sex is null || m.Sex == step.Sex)).ToArray();
            var mount = candidates.FirstOrDefault(m => m.Id == step.OwnedId) ?? (candidates.Length == 1 ? candidates[0] : null);
            if (mount is not null && step.Action is BreedingObjectiveAction.Prepare or BreedingObjectiveAction.Train && step.OwnedId != mount.Id)
            {
                progress.Steps[index] = step = step with { OwnedId = mount.Id };
                changed = true;
            }
            bool complete = step.Action switch
            {
                BreedingObjectiveAction.Capture or BreedingObjectiveAction.Buy =>
                    candidates.Count(m => step.Sex is null || m.Fertility is BreedingMountFertility.Fertile or BreedingMountFertility.Ready) >
                    progress.Steps.Take(index).Count(s => s.Action is BreedingObjectiveAction.Capture or BreedingObjectiveAction.Buy &&
                        s.RideId == step.RideId && s.Sex == step.Sex),
                BreedingObjectiveAction.Prepare => mount is { Fertility: BreedingMountFertility.Ready } && mount.Level >= (step.Level ?? 1),
                BreedingObjectiveAction.Train => mount is not null && mount.Level >= (step.Level ?? 1),
                _ => false
            };
            if (!complete) break;
            progress.Done.Add(index); changed = true;
        }
        return changed;
    }

    public static int Percent(BreedingObjectiveProgress progress) => progress.Steps.Count == 0 ? 0 :
        (int)(100L * progress.Done.Count / progress.Steps.Count);

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
        progress.Steps = nextPlan.ToList();
        progress.LastListedAt = now;
    }
}

public sealed class BreedingObjectiveJournal
{
    public string? Selected { get; set; }
    public Dictionary<string, BreedingObjectiveProgress> Goals { get; set; } = [];
    public List<BreedingPendingProduction> Pending { get; set; } = [];
}
public sealed class BreedingObjectiveProgress
{
    public BreedingPlanningOptions? Planning { get; set; }
    public List<BreedingObjectiveStep> Steps { get; set; } = [];
    public HashSet<int> Done { get; set; } = [];
    public int Cycles { get; set; }
    public bool OverrideCurrentCycle { get; set; }
    public DateTimeOffset? LastListedAt { get; set; }
}
public sealed record BreedingPendingProduction(long ItemId, DateTimeOffset ListedAt, DateTimeOffset ReviewAfter);
