using BestCrush.Domain.Models;
using BestCrush.Models;

namespace BestCrush.Services;

/// <summary>Immutable common filters shared by the module and overlay, isolated by market server.</summary>
public sealed record CrushingSearchFilters(string Text, int? LevelMin, int? LevelMax, long? JobId,
    EquipmentType[] Types, RuneFilterCriterion[] Runes, RuneFilterMatchMode RuneMatchMode)
{
    public static bool MatchesRunes(Equipment item,IReadOnlyCollection<Characteristic> criteria,RuneFilterMatchMode mode)
    {
        if(criteria.Count==0)return true;
        bool Positive(Characteristic stat)=>item.Characteristics.Any(c=>c.Characteristic==stat && ((double)c.From+c.To)/2>0);
        return mode==RuneFilterMatchMode.All?criteria.All(Positive):criteria.Any(Positive);
    }
    public static CrushingSearchFilters From(SearchItemsModel model) => new(model.SearchText ?? "", model.LevelMin, model.LevelMax,
        model.CraftJobId, model.EquipmentType.GetSelectedTypes().Order().ToArray(),
        model.RuneFilters.OrderBy(r=>r.DofusDbId).Select(r=>new RuneFilterCriterion { DofusDbId=r.DofusDbId, Characteristic=r.Characteristic, RuneName=r.RuneName }).ToArray(), model.RuneMatchMode);
    public void ApplyTo(SearchItemsModel model)
    {
        model.SearchText=Text; model.LevelMin=LevelMin; model.LevelMax=LevelMax; model.CraftJobId=JobId;
        model.EquipmentType.SetSelectedTypes(Types);
        model.RuneFilters=Runes.Select(r=>new RuneFilterCriterion { DofusDbId=r.DofusDbId, Characteristic=r.Characteristic, RuneName=r.RuneName }).ToList();
        model.RuneMatchMode=RuneMatchMode;
    }
    public bool EquivalentTo(CrushingSearchFilters other) => Text==other.Text && LevelMin==other.LevelMin && LevelMax==other.LevelMax &&
        JobId==other.JobId && RuneMatchMode==other.RuneMatchMode && Types.SequenceEqual(other.Types) &&
        Runes.Select(r=>(r.DofusDbId,r.Characteristic)).SequenceEqual(other.Runes.Select(r=>(r.DofusDbId,r.Characteristic)));
}
public sealed record CrushingSearchUpdate(string Server, CrushingSearchFilters Filters, long Version, bool Analyze, Guid Origin)
{
    public IReadOnlyList<Equipment>? Results {get;init;}
}
public sealed class CrushingSearchSynchronization
{
    private readonly object _gate=new();
    private readonly Dictionary<string,CrushingSearchUpdate> _states=new(StringComparer.OrdinalIgnoreCase);
    private long _version;
    public event Action<CrushingSearchUpdate>? Changed;
    public CrushingSearchUpdate? ForServer(string server){lock(_gate)return _states.GetValueOrDefault(server);}
    public CrushingSearchUpdate Publish(string server,CrushingSearchFilters filters,bool analyze,Guid origin)
    {
        CrushingSearchUpdate update;
        lock(_gate)
        {
            if(!analyze && _states.TryGetValue(server,out var previous) && previous.Filters.EquivalentTo(filters))return previous;
            update=new(server,filters,++_version,analyze,origin);_states[server]=update;
        }
        Changed?.Invoke(update);return update;
    }
    public bool PublishResults(string server,long version,IEnumerable<Equipment> results)
    {
        CrushingSearchUpdate update;
        lock(_gate)
        {
            if(!_states.TryGetValue(server,out var state) || state.Version!=version)return false;
            update=state with{Results=results.DistinctBy(e=>e.DofusDbId).Select(OverlayEquipmentSearchState.Lightweight).ToArray()};_states[server]=update;
        }
        Changed?.Invoke(update);return true;
    }
}
