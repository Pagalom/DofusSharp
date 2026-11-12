using BestCrush.Domain.Models;
using BestCrush.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace BestCrush.Components.Pages;

public partial class Server
{
    [Inject] public IServiceProvider ProfessionServices { get; set; } = default!;
    private CurrentCharacterState? _professionCharacters;
    private CurrentWindowProfessionsState? _professionProgress;
    private WindowServerAttributionState? _professionAttribution;
    private string _professionInputsKey = "";
    private readonly Dictionary<long, RecipeProfession> _professionRecipes = new();
    private readonly HashSet<long> _professionRecipesLoaded = new();
    private sealed record ProfessionInputs(int PlayerLevel, IReadOnlyDictionary<string, int> Levels, string Key);

    private ProfessionInputs? CaptureProfessionInputs()
    {
        if (Clients?.SelectedClient is not { HasCharacterTitle: true } client) return null;
        var profile = _professionCharacters?.Characters.FirstOrDefault(p =>
            _professionCharacters.IsCharacterConnected(p.Key) &&
            _professionCharacters.WindowForCharacter(p.Key) == client.Key &&
            string.Equals(p.Key.ServerName, ServerName, StringComparison.OrdinalIgnoreCase));
        bool attributed = string.Equals(_professionAttribution?.ForWindow(client.Key)?.ServerName,
            ServerName, StringComparison.OrdinalIgnoreCase);
        if (profile is null && !attributed) return null;
        int? level = profile?.Level ?? (attributed ? _professionAttribution?.SelectedLevelCandidate(client.Key) : null);
        var professions = _professionProgress?.ForWindow(client.Key);
        if (level is not (>= 1 and <= 200) || professions is not { Count: > 0 }) return null;
        var levels = professions.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Max(p => p.Level), StringComparer.OrdinalIgnoreCase);
        // RAM-only owner fingerprint; never logged or persisted as a character identity.
        string key = $"{client.Key.Handle}:{client.Key.ProcessId}:{level}:" +
            string.Join(';', levels.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value}"));
        return new(level.Value, levels, key);
    }
    private int? DetectedPlayerLevel => CaptureProfessionInputs()?.PlayerLevel;
    private IReadOnlyDictionary<string, int>? DetectedProfessionLevels => CaptureProfessionInputs()?.Levels;

    private void InitializeProfessionTracking()
    {
        _professionCharacters = ProfessionServices.GetService<CurrentCharacterState>();
        _professionProgress = ProfessionServices.GetService<CurrentWindowProfessionsState>();
        _professionAttribution = ProfessionServices.GetService<WindowServerAttributionState>();
        Clients.Changed += ProfessionInputsChanged;
        if (_professionCharacters is not null) _professionCharacters.Changed += ProfessionInputsChanged;
        if (_professionProgress is not null) _professionProgress.Changed += ProfessionInputsChanged;
        if (_professionAttribution is not null) _professionAttribution.Changed += ProfessionInputsChanged;
        _professionInputsKey = CaptureProfessionInputs()?.Key ?? "";
    }
    private void DisposeProfessionTracking()
    {
        Clients.Changed -= ProfessionInputsChanged;
        if (_professionCharacters is not null) _professionCharacters.Changed -= ProfessionInputsChanged;
        if (_professionProgress is not null) _professionProgress.Changed -= ProfessionInputsChanged;
        if (_professionAttribution is not null) _professionAttribution.Changed -= ProfessionInputsChanged;
    }
    private void ProfessionInputsChanged()
    {
        if (_stockDisposed) return;
        _ = InvokeAsync(() => {
            if (_stockDisposed) return;
            var inputs = CaptureProfessionInputs();
            string key = inputs?.Key ?? "";
            if (key != _professionInputsKey)
            {
                _professionInputsKey = key;
                if (Model?.RespectJobLevels == true)
                {
                    // Do not leave another character's craft accessibility on screen.
                    _allItems = null; _rankedItems = null; _unrankedItems = null;
                    _baseCalculations.Clear(); _stockPlans.Clear(); _stockResults.Clear();
                    _missingDataByItem.Clear(); _stockPreparationVersion++; _preparingStocks = false;
                    _analysisError = "Les niveaux du personnage ont changé. Relancez l’analyse.";
                    if (!CrushingProfessionFilterRules.CanEnable(inputs?.PlayerLevel, inputs?.Levels, Model.LevelMin, Model.LevelMax))
                        Model.RespectJobLevels = false;
                }
            }
            StateHasChanged();
        });
    }
    private IEnumerable<Equipment> FilterItems(IReadOnlyCollection<Equipment> items) =>
        FilterItemsBySource(items, useCatalogueRunes: true);

    private IEnumerable<Equipment> FilterOwnedItems(IReadOnlyCollection<Equipment> items) =>
        FilterItemsBySource(items, useCatalogueRunes: false);

    private IEnumerable<Equipment> FilterItemsBySource(IReadOnlyCollection<Equipment> items, bool useCatalogueRunes)
    {
        var candidates = FilterItemsCore(items, useCatalogueRunes);
        if (Model?.RespectJobLevels != true) return candidates;
        var inputs = CaptureProfessionInputs();
        if (!CrushingProfessionFilterRules.CanEnable(inputs?.PlayerLevel, inputs?.Levels, Model.LevelMin, Model.LevelMax)) return [];
        return candidates.Where(item => CrushingProfessionFilterRules.CanCraft(
            _professionRecipes.GetValueOrDefault(item.DofusDbId), inputs!.Levels));
    }
    private async Task LoadProfessionRecipesAsync(IEnumerable<Equipment> items, IServiceProvider services)
    {
        var ids = items.Select(item => item.DofusDbId).Distinct().Where(id => !_professionRecipesLoaded.Contains(id)).ToArray();
        if (ids.Length == 0) return;
        var catalog = services.GetRequiredService<ActivityCatalogService>();
        var recipes = await Task.Run(() => catalog.GetRecipeProfessionsAsync(ids, _jobFiltersStop.Token));
        if (_stockDisposed) return;
        foreach (var recipe in recipes) _professionRecipes[recipe.Key] = recipe.Value;
        _professionRecipesLoaded.UnionWith(ids);
    }
}
