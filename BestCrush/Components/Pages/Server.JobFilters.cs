using BestCrush.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BestCrush.Components.Pages;

public partial class Server
{
    private EquipmentJobFilterCatalog? _crushingJobCatalog;
    private bool _jobFiltersBusy;
    private string? _jobFiltersError;
    private readonly CancellationTokenSource _jobFiltersStop = new();
    private bool JobFilterUnavailable => Model?.CraftJobId is { } job &&
        (_jobFiltersBusy || _crushingJobCatalog?.ContainsJob(job) != true);

    private void OnCrushingJobSelectionChanged() => StateHasChanged();

    private async Task LoadCrushingJobsAsync()
    {
        if (_stockDisposed || _jobFiltersBusy) return;
        _jobFiltersBusy = true;
        _jobFiltersError = null;
        try
        {
            var token = _jobFiltersStop.Token;
            var catalog = await Task.Run(async () =>
            {
                using var scope = AnalysisScopes.CreateScope();
                var items = await scope.ServiceProvider.GetRequiredService<BestCrush.Domain.Services.ItemsService>().GetEquipmentsAsync(token);
                var jobs = await scope.ServiceProvider.GetRequiredService<ActivityCatalogService>().GetCraftingJobsAsync(token);
                return new EquipmentJobFilterCatalog(jobs, items);
            }, token);
            if (!_stockDisposed) _crushingJobCatalog = catalog;
        }
        catch (OperationCanceledException) when (_jobFiltersStop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Métiers du formulaire de concassage indisponibles.");
            if (!_stockDisposed) _jobFiltersError = "Les métiers n’ont pas pu être chargés. Choisissez Tous les métiers pour continuer, ou réessayez.";
        }
        finally
        {
            _jobFiltersBusy = false;
            if (!_stockDisposed) await InvokeAsync(StateHasChanged);
        }
    }
}
