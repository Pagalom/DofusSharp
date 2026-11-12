using BestCrush.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace BestCrush.Components.Pure;

public partial class ActivityOpportunities
{
    [Inject] private ActivityCatalogService CombatCatalogService { get; set; } = default!;

    private FarmingCombatSelection _farmCombat = FarmingCombatSelection.Empty;
    private FarmingCombatCatalog? _combatCatalog;
    private CancellationTokenSource? _combatLookup;
    private long[] _combatCatalogIds = [];
    private bool _combatCatalogRequested;
    private string? _combatCatalogError;

    private void ToggleFarmMonster(CombatSource monster)
    {
        SetFarmSource(FarmingSource.Combat);
        CombatSource[] monsters = _farmCombat.Monsters.Any(item => item.Id == monster.Id)
            ? _farmCombat.Monsters.Where(item => item.Id != monster.Id).ToArray()
            : [.. _farmCombat.Monsters, monster];
        _farmCombat = new(monsters, _farmCombat.Zones);
    }

    private void ToggleFarmZone(FarmingZone zone)
    {
        SetFarmSource(FarmingSource.Combat);
        FarmingZone[] zones = _farmCombat.Zones.Any(item => item.Id == zone.Id)
            ? _farmCombat.Zones.Where(item => item.Id != zone.Id).ToArray()
            : [.. _farmCombat.Zones, zone];
        _farmCombat = new(_farmCombat.Monsters, zones);
    }

    private async Task SortChangedAsync()
    {
        ReleaseOpenTrade();
        if (IsFarming && _sort is "monster" or "zone")
            await LoadCombatCatalogAsync();
    }

    private async Task LoadCombatCatalogAsync()
    {
        if (_disposed || _report is null || !IsFarming)
            return;

        _combatCatalogRequested = true;
        long[] ids = _report.Candidates.Where(row => row.Job is null).SelectMany(row => row.MonsterIds)
            .Distinct().Order().ToArray();
        bool sameIds = _combatCatalogIds.SequenceEqual(ids);
        if (sameIds && (_combatLookup is not null || _combatCatalog is not null && _combatCatalogError is null))
            return;

        _combatLookup?.Cancel();
        if (!sameIds) _combatCatalog = null;
        _combatCatalogIds = ids;
        _combatCatalogError = null;
        using var lookup = new CancellationTokenSource();
        _combatLookup = lookup;
        StateHasChanged();
        try
        {
            var monsters = _combatCatalog?.Monsters ??
                await CombatCatalogService.GetFarmingCombatSourcesAsync(ids, lookup.Token);
            if (_disposed || !ReferenceEquals(_combatLookup, lookup))
                return;

            _combatCatalog = new(monsters, []);
            StateHasChanged();

            if (monsters.Length == 0)
                return;

            var zones = await CombatCatalogService.GetFarmingZonesAsync(lookup.Token);
            if (_disposed || !ReferenceEquals(_combatLookup, lookup))
                return;

            var knownMonsters = monsters.Select(monster => monster.Id).ToHashSet();
            _combatCatalog = new(monsters, zones.Where(zone => zone.MonsterIds.Any(knownMonsters.Contains)).ToArray());
            if (_farmCombat.Zones.Length > 0)
            {
                var zonesById = zones.ToDictionary(zone => zone.Id);
                _farmCombat = new(_farmCombat.Monsters,
                    _farmCombat.Zones.Select(zone => zonesById.GetValueOrDefault(zone.Id, zone)).ToArray());
            }
        }
        catch (OperationCanceledException) when (lookup.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (_disposed || !ReferenceEquals(_combatLookup, lookup))
                return;
            Logger.LogWarning(ex, "Impossible de charger le catalogue des monstres et des zones.");
            _combatCatalogError = _combatCatalog is null ? "Monstres indisponibles." : "Zones indisponibles.";
        }
        finally
        {
            if (ReferenceEquals(_combatLookup, lookup))
            {
                _combatLookup = null;
                if (!_disposed) StateHasChanged();
            }
        }
    }
}
