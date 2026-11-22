using BestCrush.Domain.Models;
using BestCrush.Models;

namespace BestCrush.Services;

/// <summary>Session-only catalogue search. Keeps lightweight metadata, never recipe graphs or character data.</summary>
public sealed class OverlayEquipmentSearchState
{
    private readonly object _gate = new();
    private Equipment[] _catalogue = [];
    private CraftingJob[] _jobs = [];
    private Query? _applied;
    private RuneFilterCriterion[] _importedRunes=[];
    public string Text { get; set; } = "";
    public int? LevelMin { get; set; }
    public int? LevelMax { get; set; }
    public long? JobId { get; set; }
    public HashSet<EquipmentType> Types { get; } = [];
    public HashSet<long> RuneIds { get; } = [];
    public RuneFilterMatchMode RuneMatchMode { get; set; }
    public bool FiltersOpen { get; set; }
    public bool ResultsOpen { get; set; }
    public bool HasSearched => _applied is not null;
    public bool CatalogueReady { get; private set; }
    public bool JobsReady { get; private set; }
    public string? Error { get; internal set; }
    public string? JobsError { get; internal set; }
    public IReadOnlyList<Rune> Runes { get; private set; } = [];
    public EquipmentJobFilterCatalog? Jobs { get; private set; }
    public IReadOnlyList<Equipment> Results { get; private set; } = [];
    public int VisibleCount { get; set; } = 20;
    public int FilterCount => Types.Count + RuneIds.Count + (JobId.HasValue ? 1 : 0) + (LevelMin.HasValue ? 1 : 0) + (LevelMax.HasValue ? 1 : 0);

    internal void Load(IEnumerable<Equipment> items, IEnumerable<Rune> runes)
    {
        lock (_gate)
        {
            _catalogue = items.Select(Lightweight).ToArray();
            Runes = runes.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            CatalogueReady = true;
            Rebuild();
        }
    }
    internal void LoadJobs(IEnumerable<CraftingJob> jobs)
    {
        lock (_gate) { _jobs = jobs.ToArray(); JobsReady = true; JobsError = null; Rebuild(); }
    }
    internal void Merge(IEnumerable<Equipment> items)
    {
        lock (_gate)
        {
            var merged = _catalogue.ToDictionary(e => e.DofusDbId);
            foreach (var item in items) merged[item.DofusDbId] = Lightweight(item);
            _catalogue = merged.Values.ToArray();
            Rebuild();
        }
    }
    public void Apply()
    {
        if (LevelMin is < 0 or > 200 || LevelMax is < 0 or > 200 || LevelMin > LevelMax)
        { Error = "Niveaux : 0 à 200, minimum ≤ maximum."; return; }
        if (JobId.HasValue && Jobs?.ContainsJob(JobId.Value) != true)
        { Error = "Métier indisponible. Réessayez son chargement."; return; }
        lock (_gate)
        {
            Error = null;
            _applied = new(Text.Trim(), LevelMin, LevelMax, JobId, Types.ToHashSet(),
                Runes.Where(r => RuneIds.Contains(r.DofusDbId)).Select(r => r.Characteristic).Distinct().ToArray(), RuneMatchMode);
            VisibleCount = 20; ResultsOpen = true; FiltersOpen = false;
            Rebuild();
        }
    }
    public void ResetFilters()
    {
        LevelMin = LevelMax = null; JobId = null; Types.Clear(); RuneIds.Clear(); RuneMatchMode = RuneFilterMatchMode.Any;
    }
    public CrushingSearchFilters ExportFilters() => new(Text,LevelMin,LevelMax,JobId,Types.Order().ToArray(),
        RuneIds.Order().Select(id=>Runes.FirstOrDefault(r=>r.DofusDbId==id) is {} rune
            ? new RuneFilterCriterion{DofusDbId=id,Characteristic=rune.Characteristic,RuneName=rune.Name}
            : _importedRunes.FirstOrDefault(r=>r.DofusDbId==id)).OfType<RuneFilterCriterion>().ToArray(),RuneMatchMode);
    internal void ImportFilters(CrushingSearchFilters filters)
    {
        Text=filters.Text;LevelMin=filters.LevelMin;LevelMax=filters.LevelMax;JobId=filters.JobId;
        Types.Clear();Types.UnionWith(filters.Types);RuneIds.Clear();RuneIds.UnionWith(filters.Runes.Select(r=>r.DofusDbId));
        _importedRunes=filters.Runes;RuneMatchMode=filters.RuneMatchMode;Error=null;
    }
    internal void UseModuleResults(IReadOnlyList<Equipment> results)
    {
        lock(_gate)
        {
            bool first=_applied is null;
            var filters=ExportFilters();
            _applied=new(filters.Text.Trim(),filters.LevelMin,filters.LevelMax,filters.JobId,filters.Types.ToHashSet(),
                filters.Runes.Select(r=>r.Characteristic).Distinct().ToArray(),filters.RuneMatchMode);
            Results=results;VisibleCount=Math.Max(20,VisibleCount);Error=null;
            if(first)ResultsOpen=true;
        }
    }
    internal void ResetServer()
    {lock(_gate){ResetFilters();Text="";Results=[];_applied=null;Error=null;ResultsOpen=false;}}
    private void Rebuild()
    {
        if (JobsReady) Jobs = new(_jobs, _catalogue);
        if (_applied is not { } query) return;
        Results = _catalogue.Where(e =>
            (query.Text.Length == 0 || e.Name.Contains(query.Text, StringComparison.CurrentCultureIgnoreCase)) &&
            (!query.Min.HasValue || e.Level >= query.Min) && (!query.Max.HasValue || e.Level <= query.Max) &&
            (!query.Job.HasValue || Jobs?.Matches(e, query.Job.Value) == true) &&
            (query.Types.Count == 0 || query.Types.Contains(e.Type)) && MatchesRunes(e, query))
            .OrderByDescending(e => e.Name.Equals(query.Text, StringComparison.CurrentCultureIgnoreCase))
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(e => e.DofusDbId).ToArray();
    }
    private static bool MatchesRunes(Equipment item, Query query)
        =>CrushingSearchFilters.MatchesRunes(item,query.Runes,query.Match);
    internal static Equipment Lightweight(Equipment source)
    {
        var item = new Equipment(source.DofusDbId) { Name = source.Name, Level = source.Level, Type = source.Type, DofusDbIconId = source.DofusDbIconId };
        item.Characteristics = source.Characteristics.Select(c => new ItemCharacteristicLine(item, c.Characteristic, c.From, c.To)).ToArray();
        return item;
    }
    private sealed record Query(string Text, int? Min, int? Max, long? Job, HashSet<EquipmentType> Types, Characteristic[] Runes, RuneFilterMatchMode Match);
}
