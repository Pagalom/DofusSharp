using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BestCrush.Components.Pages;

public partial class ForgemagingWorkspace : IDisposable
{
    [Parameter] public string? ServerName { get; set; }
    private IReadOnlyList<ForgemagingRune> _runes = [];
    private ForgemagingCoachingPlan _coaching = new([], [], null);
    private ForgemagingCrushContext? _crushContext;
    private CrushingPreparationPlan? _crushPlan;
    private ForgemagingSession? _session;
    private LiveForgemagingService? _live;
    private OverlayWorkspaceService? _overlays;
    private LiveForgemagingView? _view;
    private bool _prepareCrushing, _showOwned, _latestFirst = true, _disposed;
    private string? _saveError, _validation;
    private ForgemagingOutcome _shownOdds = ForgemagingOutcome.Critical;
    private bool Busy => _view?.IsLoading == true;
    private bool Complete => _session is { } session && _validation is null && session.Lines.All(l => l.Current >= l.Target);

    protected override void OnInitialized()
    {
        using var scope = DataScopes.CreateScope();
        _live = scope.ServiceProvider.GetService<LiveForgemagingService>();
        _overlays = scope.ServiceProvider.GetService<OverlayWorkspaceService>();
        if (_live is not null) _live.Changed += LiveChanged;
    }

    protected override void OnParametersSet() => ReadLive();

    private void LiveChanged()
    {
        if (!_disposed) _ = InvokeAsync(() =>
        {
            if (_disposed) return;
            ReadLive();
            StateHasChanged();
        });
    }

    private void ReadLive()
    {
        _view = _live?.View(ServerName);
        _session = _view?.CanTrack == true ? _view.Session : null;
        _runes = _view?.Runes ?? [];
        _crushContext = _view?.Crushing;
        Recalculate();
    }

    private void Recalculate()
    {
        _overlays?.SetFmIntent(_prepareCrushing);
        _crushPlan = null;
        _coaching = new([], [], null);
        _validation = null;
        if (_session is not { } session) return;

        if (_prepareCrushing)
        {
            _validation = ForgemagingPlanner.ValidateObservedRoll(session);
            _crushPlan = CrushingPreparationPlanner.Plan(session, _runes, _crushContext);
        }
        else
        {
            _validation = ForgemagingPlanner.Validate(session);
            _coaching = ForgemagingCoach.Plan(session, _runes);
        }
    }

    private string FocusName(ForgemagingCrushValue value) => value.Focus is { } stat ? $"Focus {StatName(stat)}" : "Sans focus";
    private async Task EditedAsync()
    {
        Recalculate();
        if (_session is null || _live is null) return;
        try { await _live.UpdateGoalsAsync(_session); _saveError = null; }
        catch (Exception error)
        {
            Logger.LogWarning(error, "Could not save live FM goals");
            _saveError = "Les réglages de la session n’ont pas pu être enregistrés.";
        }
    }

    private async Task AddExtraAsync(Characteristic stat)
    {
        if (_session is null || _prepareCrushing || Busy || _session.Lines.Any(l => l.Characteristic == stat)) return;
        if(_live is null)return;
        await _live.AddExtraLineAsync(_session.GameRecordingId,stat);
        ReadLive();
    }

    private async Task RemoveExtraAsync(Characteristic stat)
    {
        if (_session is null || _live is null || _prepareCrushing || Busy) return;
        try
        {
            await _live.RemoveGoalLineAsync(_session.GameRecordingId, stat);
            _saveError = null;
            ReadLive();
        }
        catch (Exception error)
        {
            Logger.LogWarning(error, "Could not remove unused FM goal");
            _saveError = "L’objectif n’a pas pu être supprimé.";
        }
    }

    private string CrushingLink(ForgemagingSession session)
    {
        var url = $"/servers/{Uri.EscapeDataString(ServerName!)}?item={session.ItemId}&owned=true";
        return _view?.StockKey is { } key ? $"{url}&instance={Uri.EscapeDataString(key)}" : url;
    }

    private static string Money(double? value) => value is { } amount ? $"{amount:N2} K" : "Prix manquant";
    internal static string OutcomeName(ForgemagingOutcome outcome) => outcome switch
    { ForgemagingOutcome.Critical => "Succès critique", ForgemagingOutcome.Neutral => "Succès neutre", ForgemagingOutcome.Failure => "Échec", _ => "À confirmer" };
    internal static IEnumerable<(Characteristic Stat, int Delta)> Deltas(ForgemagingAttempt attempt) =>
        attempt.Before.Select(l => l.Characteristic).Union(attempt.After.Select(l => l.Characteristic))
            .Select(stat => (Stat: stat, Delta: (attempt.After.FirstOrDefault(l => l.Characteristic == stat)?.Current ?? 0) -
                (attempt.Before.FirstOrDefault(l => l.Characteristic == stat)?.Current ?? 0))).Where(x => x.Delta != 0);
    public void Dispose()
    {
        _disposed = true;
        if (_live is not null) _live.Changed -= LiveChanged;
    }
}
