using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BestCrush.Services;

public sealed partial class OverlayWorkspaceService
{
    public OverlayEquipmentSearchState EquipmentSearch { get; } = new();
    public string? EquipmentSearchServer => _searchSyncServer??ActiveGameServer;
    private readonly CrushingSearchSynchronization _crushingSearch=new();
    private readonly Guid _overlaySearchOrigin=Guid.NewGuid();
    private int _searchSyncInitialized;
    private string? _searchSyncServer;
    public CrushingSearchSynchronization CrushingSearch
    {
        get { if(Interlocked.Exchange(ref _searchSyncInitialized,1)==0)_crushingSearch.Changed+=SharedCrushingSearchChanged;return _crushingSearch; }
    }
    public void PublishEquipmentSearchFilters(bool analyze=false)
    {
        if(ActiveGameServer is not {} server)return;
        _searchSyncServer=server;
        CrushingSearch.Publish(server,EquipmentSearch.ExportFilters(),analyze,_overlaySearchOrigin);
    }
    private void ReconcileEquipmentSearchServer()
    {
        var server=ActiveGameServer;
        if(string.Equals(server,_searchSyncServer,StringComparison.OrdinalIgnoreCase))return;
        if(_searchSyncServer is not null)EquipmentSearch.ResetServer();
        _searchSyncServer=server;
        if(server is not null && CrushingSearch.ForServer(server) is {} update)SharedCrushingSearchChanged(update);
    }
    private void SharedCrushingSearchChanged(CrushingSearchUpdate update)
    {
        if(!string.Equals(update.Server,ActiveGameServer,StringComparison.OrdinalIgnoreCase))return;
        if(update.Results is {} results){EquipmentSearch.ImportFilters(update.Filters);EquipmentSearch.UseModuleResults(results,update.Indicators);Notify();_=PrepareEquipmentIndicatorsAsync();return;}
        if(update.IsAnalyzing || update.Error is not null)
        {EquipmentSearch.ImportFilters(update.Filters);EquipmentSearch.SetAnalysisStatus(update.IsAnalyzing,update.Error);Notify();return;}
        EquipmentSearch.SetAnalysisStatus(false);
        if(update.Origin==_overlaySearchOrigin)return;
        EquipmentSearch.ImportFilters(update.Filters);Notify();
        if(update.Analyze)_=ApplySharedCrushingSearchAsync(update);
    }
    private async Task ApplySharedCrushingSearchAsync(CrushingSearchUpdate update)
    {
        await PrepareEquipmentSearchAsync(update.Filters.JobId.HasValue);
        if(!string.Equals(update.Server,ActiveGameServer,StringComparison.OrdinalIgnoreCase) ||
            CrushingSearch.ForServer(update.Server)?.Version!=update.Version || !EquipmentSearch.CatalogueReady)return;
        var current=CrushingSearch.ForServer(update.Server);
        if(current?.IsAnalyzing==true || current?.Error is not null)return;
        EquipmentSearch.ImportFilters(update.Filters);EquipmentSearch.Apply();
        if(CrushingSearch.ForServer(update.Server) is {Results:{} results} completed)EquipmentSearch.UseModuleResults(results,completed.Indicators);
        Notify();
    }
    private readonly SemaphoreSlim _searchCatalogueGate = new(1, 1);
    public async Task PrepareEquipmentSearchAsync(bool includeJobs = false)
    {
        _=CrushingSearch;
        ReconcileEquipmentSearchServer();
        try
        {
            await _searchCatalogueGate.WaitAsync(_stop.Token);
            try
            {
                using var scope = scopes.CreateScope();
                var services = scope.ServiceProvider;
                if (!EquipmentSearch.CatalogueReady)
                {
                    EquipmentSearch.Error = null;
                    await using var db = await services.GetRequiredService<IDbContextFactory<BestCrushDbContext>>().CreateDbContextAsync(_stop.Token);
                    var items = await db.Equipments.AsNoTracking().Include(e => e.Characteristics).ToArrayAsync(_stop.Token);
                    var runes = await db.Runes.AsNoTracking().ToArrayAsync(_stop.Token);
                    EquipmentSearch.Load(items, runes);
                }
                if (includeJobs && !EquipmentSearch.JobsReady)
                {
                    try { EquipmentSearch.LoadJobs(await services.GetRequiredService<ActivityCatalogService>().GetCraftingJobsAsync(_stop.Token)); }
                    catch (Exception ex) when (!_stop.IsCancellationRequested)
                    { EquipmentSearch.JobsError = "Métiers indisponibles"; logger.LogWarning(ex, "Overlay craft job catalogue unavailable"); }
                }
            }
            finally { _searchCatalogueGate.Release(); }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex)
        { EquipmentSearch.Error = "Recherche indisponible. Réessayez."; logger.LogWarning(ex, "Overlay equipment catalogue unavailable"); }
        finally { Notify(); }
    }
    public void PublishEquipmentSearchCatalogue(IEnumerable<Equipment> items)
    {
        EquipmentSearch.Merge(items);
        Notify();
    }
    public async Task<bool> FocusSearchEquipmentAsync(long id)
    {
        var server = ActiveGameServer;
        var window = TargetWindow;
        if (server is null) { EquipmentSearch.Error = "Sélectionnez le serveur joué dans BestCrush."; Notify(); return false; }
        var intent = Interlocked.Increment(ref _focusIntent);
        try
        {
            using var scope = scopes.CreateScope();
            var item = await scope.ServiceProvider.GetRequiredService<ItemsService>().GetEquipmentAsync(id, _stop.Token);
            if (item is null || intent != FocusVersion || server != ActiveGameServer || window != TargetWindow) return false;
            EquipmentSearch.Error = null;
            await FocusAsync(item, server);
            return true;
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return false; }
        catch (Exception ex)
        { EquipmentSearch.Error = "Objet indisponible. Réessayez."; logger.LogWarning(ex, "Overlay search focus unavailable for {ItemId}", id); Notify(); return false; }
    }
}
