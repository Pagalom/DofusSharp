using BestCrush.Domain.Models;
using BestCrush.Services;
using Microsoft.Extensions.Logging;

namespace BestCrush.Components.Pure;

public partial class StockOpportunities
{
    private string _craftJob = "";
    private IReadOnlyCollection<long> _craftTypes = [];
    private bool _showOtherCraftTypes;
    private bool _respectCraftJobs, _recipeMetadataBusy;
    private string? _recipeMetadataError;
    private long _recipeMetadataVersion;
    private readonly Dictionary<long, RecipeProfession> _recipeProfessions = new();
    private readonly HashSet<long> _loadedRecipeItems = new();
    private sealed record CraftOptionsKey(StockOpportunityReport Report, long MetadataVersion, string Job);
    private CraftOptionsKey? _craftOptionsKey;
    private string[] _craftJobs = [];
    private sealed record CraftTypeOption(long Id, string Label, string? Image, bool IsEquipment = false);
    private CraftTypeOption[] _craftAvailableTypes = [];

    private IReadOnlyDictionary<string, int>? CraftJobLevels =>
        StockOpportunityService.CanUse(Stocks.Selected, Server)
            ? Professions.ForWindow(Clients.SelectedClient?.Key)?.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Max(p => p.Level), StringComparer.OrdinalIgnoreCase)
            : null;

    private string CraftLevelsKey => !_respectCraftJobs || OriginFilter != "craft" ? "" :
        string.Join(';', (CraftJobLevels ?? new Dictionary<string, int>()).OrderBy(x => x.Key)
            .Select(x => $"{x.Key}:{x.Value}"));

    private IReadOnlyList<string> CraftJobOptions { get { PrepareCraftOptions(); return _craftJobs; } }
    private IReadOnlyList<CraftTypeOption> CraftTypeOptions { get { PrepareCraftOptions(); return _craftAvailableTypes; } }

    private void PrepareCraftOptions()
    {
        if (_report is null) return;
        var key = new CraftOptionsKey(_report, _recipeMetadataVersion, _craftJob);
        if (key == _craftOptionsKey) return;
        var rows = _report.Candidates.Where(x => x.Origin == "craft").ToArray();
        _craftJobs = rows.Select(x => JobFor(x.Plan.Product))
            .Append(_craftJob).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
        _craftAvailableTypes = rows.Select(row => row.Plan.Product).Where(MatchesCraftJob).Select(TypeFor)
            .DistinctBy(type => type.Id).OrderBy(type => type.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
        _craftOptionsKey = key;
    }

    private RecipeProfession? ProfessionFor(IItem item) => item is CraftedProduct product
        ? new RecipeProfession(product.DofusDbId, product.Job, product.JobLevel)
        : _recipeProfessions.GetValueOrDefault(item.DofusDbId);

    private string? JobFor(IItem item) => item is CraftedProduct product ? product.Job :
        _recipeProfessions.GetValueOrDefault(item.DofusDbId)?.Job;

    private static CraftTypeOption TypeFor(IItem item) => item switch
    {
        Equipment equipment => new(equipment.Type.ToDofusDbItemTypeId(), equipment.Type.ToDisplayName(),
            EquipmentTypeFilter.TypeImage(equipment.Type), IsEquipment: true),
        CraftedProduct product => new(product.TypeId, product.TypeName, null),
        _ => new(0, "Autre produit", null)
    };

    private bool MatchesCraftJob(IItem item) => _craftJob.Length == 0 ||
        string.Equals(JobFor(item), _craftJob, StringComparison.OrdinalIgnoreCase);

    private bool MatchesCraftFilters(EconomicOpportunity row, IReadOnlyDictionary<string, int>? levels)
    {
        var item = row.Plan.Product;
        if (!MatchesCraftJob(item) || (_craftTypes.Count > 0 && !_craftTypes.Contains(TypeFor(item).Id))) return false;
        if (!_respectCraftJobs) return true;
        // Unknown prerequisites never imply that a recipe is accessible.
        return ProfessionFor(item) is { Job: { } job, Level: > 0 } recipe && levels is not null &&
            levels.TryGetValue(job, out int level) && level >= recipe.Level;
    }

    private void SelectCraftJob(string job)
    {
        _craftJob = job;
        CraftJobChanged();
    }
    private void CraftJobChanged() => _craftTypes = [];
    private void ToggleCraftType(long id) => _craftTypes = _craftTypes.Contains(id)
        ? _craftTypes.Where(type => type != id).ToArray() : [.. _craftTypes, id];
    private void JobsChanged() { if (!_disposed) _ = InvokeAsync(StateHasChanged); }
    private Task RetryCraftMetadataAsync() => _report is { } report && !_recipeMetadataBusy
        ? LoadCraftMetadataAsync(report, _stop.Token) : Task.CompletedTask;

    private async Task LoadCraftMetadataAsync(StockOpportunityReport report, CancellationToken token)
    {
        if (OriginFilter != "craft") return;
        long[] ids = report.Candidates.Where(x => x.Origin == "craft" && x.Plan.Product is Equipment).Select(x => x.Plan.Product.DofusDbId)
            .Distinct().Where(x => !_loadedRecipeItems.Contains(x)).ToArray();
        if (ids.Length == 0) return;
        _recipeMetadataBusy = true;
        _recipeMetadataError = null;
        StateHasChanged();
        try
        {
            var recipes = await Catalog.GetRecipeProfessionsAsync(ids, token);
            token.ThrowIfCancellationRequested();
            if (_disposed) return;
            foreach (var pair in recipes) _recipeProfessions[pair.Key] = pair.Value;
            _loadedRecipeItems.UnionWith(ids);
            _recipeMetadataVersion++;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Métiers des recettes de craft indisponibles.");
            if (!_disposed && ReferenceEquals(_report, report))
                _recipeMetadataError = "Les métiers des recettes n’ont pas pu être chargés. Le filtre par type reste disponible.";
        }
        finally
        {
            if (ReferenceEquals(_report, report)) _recipeMetadataBusy = false;
            if (!_disposed) StateHasChanged();
        }
    }
}
