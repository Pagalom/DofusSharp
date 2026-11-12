using BestCrush.Domain.Models;
using BestCrush.Models;

namespace BestCrush.Services;

/// <summary>Public craft membership joined to the local equipment catalogue, independent of prices and characters.</summary>
public sealed class EquipmentJobFilterCatalog
{
    private readonly Dictionary<long, HashSet<long>> _items = new();
    private readonly Dictionary<long, EquipmentType[]> _types = new();
    public IReadOnlyList<CraftingJob> Jobs { get; }

    public EquipmentJobFilterCatalog(IEnumerable<CraftingJob> jobs, IReadOnlyCollection<Equipment> equipment)
    {
        var supported = EquipmentTypesFilter.SupportedTypes.ToHashSet();
        var byId = equipment.Where(x => supported.Contains(x.Type)).DistinctBy(x => x.DofusDbId).ToDictionary(x => x.DofusDbId);
        List<CraftingJob> available = [];
        foreach (var job in jobs)
        {
            // The public Base group (job 1) is not a player profession.
            if (job.Id == 1) continue;
            var ids = job.ItemIds.Where(byId.ContainsKey).ToHashSet();
            if (ids.Count == 0) continue;
            _items[job.Id] = ids;
            _types[job.Id] = ids.Select(id => byId[id].Type).Distinct().OrderBy(x => x.ToDisplayName()).ToArray();
            available.Add(job);
        }
        Jobs = available.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public bool ContainsJob(long job) => _items.ContainsKey(job);
    public bool Matches(Equipment item, long job) => _items.TryGetValue(job, out var ids) && ids.Contains(item.DofusDbId);
    public IReadOnlyCollection<EquipmentType> TypesFor(long? job) => job is null ? EquipmentTypesFilter.SupportedTypes :
        _types.GetValueOrDefault(job.Value) ?? Array.Empty<EquipmentType>();
}
