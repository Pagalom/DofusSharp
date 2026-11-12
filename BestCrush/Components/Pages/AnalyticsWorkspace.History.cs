using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BestCrush.Components.Pages;

public partial class AnalyticsWorkspace
{
    private readonly BackgroundDataLoad<MarketAnalyticsHistoryPage> _historyLoad = new();
    private MarketAnalyticsHistoryPage? _history;
    private MarketAnalyticsMember? _historyMember;
    private string _historySearch = "";
    private bool _historyTab, _historyAll;
    private int _historyPage;
    private DateTime _historyTo;
    private string? _historyError;
    private string HistoryMemberValue
    {
        get => _historyMember is { } m ? $"{(int)m.ObjectType}:{m.DofusDbId}" : "";
        set => _historyMember = Inspected?.Members.Select(m => m.Member).FirstOrDefault(m => $"{(int)m.ObjectType}:{m.DofusDbId}" == value);
    }
    private IReadOnlyList<MarketAnalyticsMember> HistoryChoices => (Inspected?.Members ?? [])
        .Select(m => m.Member).Where(m => m.Name.Contains(_historySearch.Trim(), StringComparison.CurrentCultureIgnoreCase))
        .OrderBy(m => m.Name).Take(50).Concat(_historyMember is { } selected ? [selected] : [])
        .DistinctBy(m => m.Key).OrderBy(m => m.Name).ToArray();

    private async Task OpenDetailsAsync(Guid id)
    {
        ShowDetails(id);
        if (_series.First(s => s.Id == id).SelectionMode == MarketAnalyticsSelectionMode.Single)
            await OpenHistoryAsync(Inspected?.Members.FirstOrDefault()?.Member);
    }
    private async Task OpenHistoryAsync(MarketAnalyticsMember? member = null)
    {
        _historyTab = true;
        _historyMember = member ?? _historyMember ?? Inspected?.Members.FirstOrDefault()?.Member;
        _historySearch = "";
        await ReloadHistoryAsync();
    }
    private void ShowObjects()
    {
        _historyTab = false; _historyLoad.Cancel(); _history = null; _historyError = null;
    }
    private Task ReloadHistoryAsync()
    {
        _historyPage = 0;
        _historyTo = _historyAll ? DateTime.UtcNow : _comparison?.ToUtc ?? DateTime.UtcNow;
        return LoadHistoryAsync();
    }
    private async Task ChangeHistoryPageAsync(int delta)
    {
        if (_historyLoad.IsLoading || _historyPage + delta < 0 || (delta > 0 && _history?.HasMore != true)) return;
        _historyPage += delta;
        await LoadHistoryAsync();
    }
    private async Task LoadHistoryAsync()
    {
        _history = null; _historyError = null;
        if (_disposed || !_historyTab || _panel != AnalyticsPanel.Details || _historyMember is not { } member) return;
        var server = ServerName;
        DateTime? from = _historyAll ? null : _comparison?.FromUtc;
        var to = _historyTo; int page = _historyPage;
        bool loaded = await _historyLoad.RunAsync(async token =>
        {
            using var scope = DataScopes.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<MarketAnalyticsService>()
                .GetObservationHistoryAsync(server, member.Key, from, to, page, token: token);
        });
        if (_disposed) return;
        if (loaded && _panel == AnalyticsPanel.Details && _historyTab && server == ServerName)
            _history = _historyLoad.Value;
        if (_historyLoad.Error is { } error)
        {
            Logger.LogWarning(error, "Could not load observation history for {Item}", member.Key);
            _historyError = "L’historique n’a pas pu être chargé. Réessayez.";
        }
    }
    private void ResetHistory()
    {
        _historyLoad.Cancel(); _history = null; _historyMember = null; _historySearch = "";
        _historyTab = false; _historyAll = false; _historyPage = 0; _historyError = null;
    }
}
