using System.Text.Json;
using BestCrush.Domain.Services;

namespace BestCrush.Services;

public sealed class PetObjectiveProgressStore(string path)
{
    private readonly object _sync = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal PetObjectiveJournal Read(string scope)
    {
        lock (_sync) return Load().GetValueOrDefault(scope) ?? new();
    }
    internal void Save(string scope, PetObjectiveJournal journal)
    {
        lock (_sync)
        {
            var all = Load(); all[scope] = journal;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temp, JsonSerializer.Serialize(all, Json)); File.Move(temp, path, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
    private Dictionary<string, PetObjectiveJournal> Load()
    {
        if (!File.Exists(path)) return [];
        try
        {
            var all = JsonSerializer.Deserialize<Dictionary<string, PetObjectiveJournal>>(File.ReadAllText(path), Json)
                ?? throw new JsonException();
            if (all.Values.Any(j => j is null || j.Goals is null || j.Listings is null || j.Sales is null || j.Returns is null ||
                j.Goals.Values.Any(g => g is null || g.Supplies is null || g.PendingListings is null || g.ExistingUids is null || g.Percent is < 0 or > 100))) throw new JsonException();
            return all;
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        { throw new InvalidDataException("Suivi des familiers illisible. Le fichier est conservé.", e); }
    }

    internal static string Key(PetAssessment pet) => pet.Owned is { } own ? $"owned:{own.Uid}"
        : $"buy:{pet.Species.Id}:{pet.Purchase!.Variant}:{pet.Best!.Level}";

    internal static void Observe(PetObjectiveJournal journal, PetOpportunityReport report,
        IReadOnlyList<ProductionListing> listings, DateTimeOffset now, IReadOnlyList<ProductionSale>? sales = null, IReadOnlyList<ProductionReturn>? returns = null)
    {
        // Bind an acquired pet to one purchase objective before creating an owned objective for it.
        var claimed = journal.Goals.Values.Where(g => g.Uid.HasValue).Select(g => g.Uid!.Value).ToHashSet();
        foreach (var row in report.Purchases)
            if (!journal.Goals.Values.Any(g => g.Purchase && g.ItemId == row.Species.Id && g.Variant == row.Purchase!.Variant && g.TargetLevel == row.Best!.Level && g.ListingId is null))
            {
                var goal = Create(row, now);
                goal.ExistingUids = report.Owned.Select(p => p.Owned!.Uid).ToHashSet();
                string key = journal.Goals.ContainsKey(Key(row)) ? Key(row) + ":" + Guid.NewGuid().ToString("N") : Key(row); journal.Goals.Add(key, goal);
            }
        foreach (var goal in journal.Goals.Values.Where(g => g.Purchase && g.Uid is null && g.ListingId is null))
        {
            var matches = report.Owned.Where(p => p.Species.Id == goal.ItemId && p.Owned!.Reading is { } reading &&
                reading.Variant == goal.Variant && reading.Level <= goal.TargetLevel && !claimed.Contains(p.Owned.Uid) && !goal.ExistingUids.Contains(p.Owned.Uid) &&
                !listings.Any(l => journal.Listings.Contains(l.Id) && l.InstanceUid == p.Owned.Uid)).ToArray();
            if (matches.FirstOrDefault() is { } acquired)
            { goal.Uid = acquired.Owned!.Uid; claimed.Add(goal.Uid.Value); }
        }
        foreach (var row in report.Owned.Where(p => p.Best is not null && p.Owned!.Reading?.Experience is not null))
            if (!claimed.Contains(row.Owned!.Uid) && !listings.Any(l => journal.Listings.Contains(l.Id) && l.InstanceUid == row.Owned.Uid))
            { journal.Goals[Key(row)] = Create(row, now); claimed.Add(row.Owned.Uid); }

        foreach (var goal in journal.Goals.Values)
        {
            var pet = report.Owned.FirstOrDefault(p => p.Owned!.Uid == goal.Uid && p.Species.Id == goal.ItemId &&
                p.Owned.Reading?.Variant == goal.Variant);
            if (pet?.Owned?.Reading is { Experience: { } xp } reading)
            {
                if (reading.Level > goal.TargetLevel && pet!.Best is { } revised)
                {
                    goal.TargetLevel = revised.Level; goal.TargetExperience = PetExperience.RequiredAtLevel(revised.Level);
                    goal.Supplies = revised.Nutrition.Meals.GroupBy(m => m.Food.Id).ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity));
                }
                double training = goal.TargetExperience <= goal.StartExperience ? 1 :
                    Math.Clamp((double)(xp - goal.StartExperience) / (goal.TargetExperience - goal.StartExperience), 0, 1);
                double supplies = goal.Supplies.Count == 0 ? 1 : goal.Supplies.Average(s =>
                    Math.Clamp((double)(report.Inventory.GetValueOrDefault(s.Key) + report.Bank.GetValueOrDefault(s.Key)) / s.Value, 0, 1));
                // Once feeding starts, consumed supplies must not make progress go backwards.
                int percent = 25 + (int)(15 * Math.Max(supplies, training)) + (int)(50 * training);
                goal.Percent = Math.Max(goal.Percent, Math.Min(90, percent));
                goal.CurrentLevel = reading.Level;
            }
            var matchingListings = listings.Where(l => !journal.Listings.Contains(l.Id) && l.At >= goal.StartedAt &&
                l.ItemId == goal.ItemId && l.InstanceUid == goal.Uid && l.Level == goal.TargetLevel && l.Variant == goal.Variant).ToArray();
            foreach (var listing in matchingListings)
            {
                journal.Listings.Add(listing.Id); goal.LastListedAt = listing.At;
                goal.PendingListings[listing.Id] = listing.At; goal.ListingId = listing.Id;
            }
            foreach (var sold in sales ?? [])
            {
                if (!goal.PendingListings.TryGetValue(sold.ListingId, out var listedAt) || sold.At < listedAt ||
                    !journal.Sales.Add(sold.Id)) continue;
                goal.PendingListings.Remove(sold.ListingId); goal.Cycles++; goal.SoldAt = sold.At;
            }
            foreach (var returned in returns ?? [])
            {
                if (!goal.PendingListings.TryGetValue(returned.ListingId, out var listedAt) || returned.At < listedAt ||
                    !journal.Returns.Add(returned.Id)) continue;
                goal.PendingListings.Remove(returned.ListingId); goal.ReturnedAt = returned.At;
            }
            if (goal.PendingListings.Count > 0) goal.Percent = pet?.Owned is null ? 95 : Math.Min(90, goal.Percent);
            else if (goal.ReturnedAt is not null && (goal.SoldAt is null || goal.ReturnedAt > goal.SoldAt))
            { goal.Percent = pet?.Owned is null ? 90 : Math.Min(90, goal.Percent); goal.ListingId = null; }
            else if (goal.SoldAt is not null && pet?.Owned is null) goal.Percent = 100;

        }
    }

    private static PetObjectiveProgress Create(PetAssessment pet, DateTimeOffset now) => new()
    {
        ItemId = pet.Species.Id, Name = pet.Species.Name, Purchase = pet.Owned is null, Uid = pet.Owned?.Uid,
        Variant = pet.Owned?.Reading?.Variant ?? pet.Purchase!.Variant, TargetLevel = pet.Best!.Level,
        StartExperience = pet.Owned?.Reading?.Experience ?? pet.Purchase!.Experience,
        TargetExperience = PetExperience.RequiredAtLevel(pet.Best.Level), StartedAt = now,
        Supplies = pet.Best.Nutrition.Meals.GroupBy(m => m.Food.Id).ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity))
    };
}

internal sealed class PetObjectiveJournal
{
    public Dictionary<string, PetObjectiveProgress> Goals { get; set; } = [];
    public HashSet<string> Listings { get; set; } = [];
    public HashSet<string> Sales { get; set; } = [];
    public HashSet<string> Returns { get; set; } = [];
}
internal sealed class PetObjectiveProgress
{
    public long ItemId { get; set; }
    public string Name { get; set; } = "";
    public bool Purchase { get; set; }
    public ulong? Uid { get; set; }
    public HashSet<ulong> ExistingUids { get; set; } = [];
    public string Variant { get; set; } = "normal";
    public int TargetLevel { get; set; }
    public int? CurrentLevel { get; set; }
    public long StartExperience { get; set; }
    public long TargetExperience { get; set; }
    public Dictionary<long, int> Supplies { get; set; } = [];
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? LastListedAt { get; set; }
    public string? ListingId { get; set; }
    public DateTimeOffset? SoldAt { get; set; }
    public DateTimeOffset? ReturnedAt { get; set; }
    public Dictionary<string, DateTimeOffset> PendingListings { get; set; } = [];
    public int Percent { get; set; }
    public int Cycles { get; set; }
}
