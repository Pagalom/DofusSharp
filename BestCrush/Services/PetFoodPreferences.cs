using System.Collections.Frozen;
using System.Numerics;

namespace BestCrush.Services;

public sealed record PetFoodSelection(bool UseStocks,IReadOnlySet<long> Skipped,long Revision)
{
    public static PetFoodSelection Default {get;}=new(true,Array.Empty<long>().ToFrozenSet(),0);
    internal IReadOnlyDictionary<long,BigInteger> PlanningStock(IReadOnlyDictionary<long,BigInteger> stock) =>
        UseStocks ? stock.Where(pair=>!Skipped.Contains(pair.Key)).ToDictionary() : new Dictionary<long,BigInteger>();
}

// Session-only choices shared by the page, overlay and their common analysis.
public sealed class PetFoodPreferences
{
    private readonly object _gate=new();
    private PetFoodSelection _selection=PetFoodSelection.Default;
    private readonly List<long> _history=[];
    public PetFoodSelection Selection {get {lock(_gate)return _selection;}}
    public event Action? Changed;
    public void SetUseStocks(bool use)
    {
        lock(_gate){if(_selection.UseStocks==use)return;_selection=_selection with {UseStocks=use,Revision=_selection.Revision+1};}
        Changed?.Invoke();
    }
    public void Skip(long id)
    {
        lock(_gate)
        {
            if(id<=0 || _selection.Skipped.Contains(id))return;
            _history.Add(id);
            _selection=_selection with {Skipped=_selection.Skipped.Append(id).ToFrozenSet(),Revision=_selection.Revision+1};
        }
        Changed?.Invoke();
    }
    public void Previous()
    {
        lock(_gate)
        {
            if(_history.Count==0)return;
            long id=_history[^1];_history.RemoveAt(_history.Count-1);
            _selection=_selection with {Skipped=_selection.Skipped.Where(food=>food!=id).ToFrozenSet(),Revision=_selection.Revision+1};
        }
        Changed?.Invoke();
    }
    public void Reset()
    {
        lock(_gate)
        {
            if(_selection.Skipped.Count==0)return;
            _history.Clear();
            _selection=_selection with {Skipped=PetFoodSelection.Default.Skipped,Revision=_selection.Revision+1};
        }
        Changed?.Invoke();
    }
}
