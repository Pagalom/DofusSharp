using System.Text.Json.Serialization;

namespace BestCrush.Services;

public sealed record FarmingZone(long Id, string Name, string? Region, long[] MonsterIds);

public sealed class FarmingCombatSelection(CombatSource[] monsters, FarmingZone[] zones)
{
    public static FarmingCombatSelection Empty { get; } = new([], []);
    public CombatSource[] Monsters { get; } = monsters;
    public FarmingZone[] Zones { get; } = zones;
    [JsonIgnore] public int Count => Monsters.Length + Zones.Length;
    [JsonIgnore] public IReadOnlySet<long> MonsterIds { get; } =
        monsters.Select(monster => monster.Id).Concat(zones.SelectMany(zone => zone.MonsterIds)).ToHashSet();

    public bool IncludesMonster(long id) => Count == 0 || MonsterIds.Contains(id);
}

public sealed class FarmingCombatCatalog(CombatSource[] monsters, FarmingZone[] zones)
{
    private static readonly StringComparer NameComparer = StringComparer.CurrentCultureIgnoreCase;
    private readonly Dictionary<long, CombatSource> _monsters = monsters.DistinctBy(monster => monster.Id).ToDictionary(monster => monster.Id);
    private readonly ILookup<long, FarmingZone> _zonesByMonster = zones
        .SelectMany(zone => zone.MonsterIds.Select(id => (Id: id, Zone: zone)))
        .ToLookup(entry => entry.Id, entry => entry.Zone);

    public CombatSource[] Monsters { get; } = monsters;
    public FarmingZone[] Zones { get; } = zones;

    public CombatSource? Monster(long id) => _monsters.GetValueOrDefault(id);
    public IEnumerable<FarmingZone> ZonesFor(long monsterId) => _zonesByMonster[monsterId];

    public string? MonsterNameFor(IReadOnlyList<long> monsterIds, FarmingCombatSelection selection) =>
        monsterIds.Where(selection.IncludesMonster).Select(id => Monster(id)?.Name)
            .Where(name => name is not null).Min(NameComparer);

    public string? ZoneNameFor(IReadOnlyList<long> monsterIds, FarmingCombatSelection selection)
    {
        // Prefer the location the player selected when a monster occurs in several zones.
        var selectedZone = selection.Zones.Where(zone => zone.MonsterIds.Any(monsterIds.Contains))
            .Select(zone => zone.Name).Min(NameComparer);
        return selectedZone ?? monsterIds.Where(selection.IncludesMonster).SelectMany(ZonesFor)
            .Select(zone => zone.Name).Min(NameComparer);
    }
}
