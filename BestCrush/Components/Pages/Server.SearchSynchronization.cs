using BestCrush.Domain.Models;
using BestCrush.Models;
using BestCrush.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BestCrush.Components.Pages;

public partial class Server
{
    private readonly Guid _sharedSearchOrigin=Guid.NewGuid();
    private OverlayWorkspaceService? _sharedSearchHost;
    private long _commonSearchVersion,_analyzedCommonSearchVersion=-1;
    private bool _pendingSharedAnalysis,_runningSharedAnalysis;
    private void InitializeSharedCrushingSearch()
    {
        _sharedSearchHost=OverlayServices?.GetService<OverlayWorkspaceService>();
        if(_sharedSearchHost is not null)_sharedSearchHost.CrushingSearch.Changed+=OnSharedCrushingSearch;
    }
    private void OnSharedCrushingSearch(CrushingSearchUpdate update)
    {
        if(_stockDisposed || update.Origin==_sharedSearchOrigin || update.Results is not null || update.IsAnalyzing || update.Error is not null ||
            !string.Equals(update.Server,ServerName,StringComparison.OrdinalIgnoreCase) || _loadedServerName!=ServerName)return;
        _=InvokeAsync(async()=>
        {
            if(_stockDisposed || !string.Equals(update.Server,ServerName,StringComparison.OrdinalIgnoreCase) || update.Version<=_commonSearchVersion)return;
            Model ??=new SearchItemsModel();update.Filters.ApplyTo(Model);_commonSearchVersion=update.Version;
            _pendingSharedAnalysis=update.Analyze;StateHasChanged();await RunPendingSharedAnalysisAsync();
        });
    }
    private void ReconcileSharedCrushingSearch()
    {
        if(_sharedSearchHost is null || Model is null)return;
        if(_sharedSearchHost.CrushingSearch.ForServer(ServerName) is {} update)
        {
            bool changed=!update.Filters.EquivalentTo(CrushingSearchFilters.From(Model));
            update.Filters.ApplyTo(Model);_commonSearchVersion=update.Version;
            _pendingSharedAnalysis=update.Analyze && (_allItems is null || changed);
            if(_allItems is not null && !changed && !update.IsAnalyzing && update.Error is null)
            {
                _analyzedCommonSearchVersion=update.Version;
                PublishSharedCrushingResults();
            }
        }
        else PublishCommonCrushingFilters();
    }
    private void PublishCommonCrushingFilters(bool analyze=false)
    {
        if(_sharedSearchHost is null || Model is null || string.IsNullOrWhiteSpace(ServerName))return;
        _commonSearchVersion=_sharedSearchHost.CrushingSearch.Publish(ServerName,CrushingSearchFilters.From(Model),analyze,_sharedSearchOrigin).Version;
    }
    private void CommonCrushingFiltersEdited()
    {PublishCommonCrushingFilters();StateHasChanged();}
    private async Task RunPendingSharedAnalysisAsync()
    {
        if(!_pendingSharedAnalysis || _runningSharedAnalysis || _stockDisposed || _searching || (_jobFiltersBusy && Model?.CraftJobId is not null))return;
        _runningSharedAnalysis=true;
        try
        {
            if(JobFilterUnavailable)await LoadCrushingJobsAsync();
            if(JobFilterUnavailable || _stockDisposed){_pendingSharedAnalysis=false;return;}
            _pendingSharedAnalysis=false;
            await AnalyzeAsync();
        }
        finally
        {
            _runningSharedAnalysis=false;
            if(_pendingSharedAnalysis && !_stockDisposed && !_searching && (!_jobFiltersBusy || Model?.CraftJobId is null))_=InvokeAsync(RunPendingSharedAnalysisAsync);
        }
    }
    private void PublishSharedCrushingResults()
    {
        if(_sharedSearchHost is null || _rankedItems is null || _unrankedItems is null ||
            _analyzedCommonSearchVersion!=_commonSearchVersion)return;
        var ordered=OrderResults(_rankedItems.GroupBy(i=>i.Equipment).Select(SelectBestScenarioForCurrentSort))
            .Where(i=>MatchesSearchText(i.Equipment)).ToArray();
        var ranked=ordered.Where(i=>!IsPartial(i)).Concat(ordered.Where(IsPartial)).Select(i=>i.Equipment);
        var results=ranked.Concat(OrderUnrankedItems(_unrankedItems)).ToArray();
        var indicators=results.ToDictionary(item=>item.DofusDbId,item=>OverlayMarketFreshness.From(item,_baseCalculations.GetValueOrDefault(item.DofusDbId)));
        _sharedSearchHost.CrushingSearch.PublishResults(ServerName,_commonSearchVersion,results,indicators);
    }
}
