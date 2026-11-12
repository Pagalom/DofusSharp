using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using Microsoft.AspNetCore.Components;

namespace BestCrush.Components.Pages;

public partial class RoutineWorkspace
{
    [Parameter] public string? ServerName { get; set; }
    private WeeklyRoutine? _routine, _saved;
    private DateOnly _today = WeeklyRoutinePlanner.Today(DateTimeOffset.UtcNow), _day;
    private bool _loading, _saving, _showAside, _settingsOpen, _resetOpen, _disposed;
    private string _newTitle = "";
    private int _newPriority = 2;
    private Guid? _editingId, _deletedId;
    private Dictionary<Guid, int> _editingPriorities = [];
    private Task _saveTask = Task.CompletedTask;
    private bool HasRoom => _routine is { } r && r.Activities.Count < 10000 && r.Activities.Count(a => !a.Deleted) < 200;
    private bool HistoricalDeletion(RoutineActivity activity) => _day < _today && activity.Deleted && WeeklyRoutinePlanner.DoneOn(_routine!, activity, _day);
    private IEnumerable<RoutineActivity> DisplayedActivities => (_routine?.Activities ?? [])
        .Where(a => WeeklyRoutinePlanner.Eligible(_routine!, a) || HistoricalDeletion(a) || _showAside && !a.Deleted || a.Id == _editingId)
        .OrderBy(a => _editingId.HasValue ? _editingPriorities.GetValueOrDefault(a.Id, a.Priority) : a.Priority);
    private string? _error;
    private readonly CancellationTokenSource _stop = new();
    private IEnumerable<RoutineActivity> Included => _routine?.Activities.Where(a => WeeklyRoutinePlanner.Eligible(_routine, a) || HistoricalDeletion(a)) ?? [];
    private int DoneCount => Included.Count(a => WeeklyRoutinePlanner.DoneOn(_routine!, a, _day));
    private int GoalCount => Included.Count();
    private bool HasMissingEstimates => Included.Any(a => a.EstimatedNetKamas is null && !WeeklyRoutinePlanner.DoneOn(_routine!, a, _day));
    private long? RemainingEstimate => Included.Any(a => a.EstimatedNetKamas.HasValue)
        ? Included.Where(a => !WeeklyRoutinePlanner.DoneOn(_routine!, a, _day)).Sum(a => a.EstimatedNetKamas ?? 0) : null;
    private static string Money(long? amount) => amount is { } value ? $"{value:N0} K" : "À renseigner";

    protected override async Task OnInitializedAsync() { await LoadAsync(); _ = WatchDateAsync(_stop.Token); }
    private async Task LoadAsync()
    {
        _loading = true; _error = null;
        try { var value = await Task.Run(Store.Read); if (!_disposed) { _routine = value; _saved = BestCrush.Services.WeeklyRoutineStore.Copy(value); _day = _today; } }
        catch (Exception error) { if (!_disposed) _error = $"La routine n’a pas pu être chargée. {error.Message}"; }
        finally { _loading = false; }
    }
    private Task SaveAsync()
    {
        if (_routine is null || _disposed) return Task.CompletedTask;
        if (_saving) return _saveTask;
        _saving = true; _error = null;
        _saveTask = PersistAsync(BestCrush.Services.WeeklyRoutineStore.Copy(_routine));
        return _saveTask;
    }
    private async Task PersistAsync(WeeklyRoutine copy)
    {
        try { await Task.Run(() => Store.Save(copy)); _saved = copy; }
        catch (Exception error) { _routine = _saved is null ? null : BestCrush.Services.WeeklyRoutineStore.Copy(_saved); _error = $"Modification non enregistrée. {error.Message}"; _day = _today; }
        finally { _saving = false; }
    }
    private async Task ToggleEditorAsync(Guid id)
    {
        await _saveTask;
        if (_disposed || _routine is null) return;
        bool close = _editingId == id;
        _editingId = null;
        _editingPriorities.Clear();
        if (close || !_routine.Activities.Any(a => a.Id == id && !a.Deleted)) return;
        _editingPriorities = _routine.Activities.ToDictionary(a => a.Id, a => a.Priority);
        _editingId = id;
    }
    private async Task DeleteActivityAsync(Guid id)
    {
        await _saveTask;
        if (_routine is null || _disposed || _editingId != id) return;
        var activity = _routine.Activities.FirstOrDefault(a => a.Id == id && !a.Deleted);
        if (activity is null) return;
        activity.Deleted = true;
        await SaveAsync();
        if (_error is null) { _deletedId = id; _editingId = null; _editingPriorities.Clear(); }
    }
    private async Task UndoDeleteAsync()
    {
        await _saveTask;
        if (_routine is null || _deletedId is not { } id || !HasRoom) return;
        var activity = _routine.Activities.FirstOrDefault(a => a.Id == id);
        if (activity is null) return;
        activity.Deleted = false;
        await SaveAsync();
        if (_error is null) _deletedId = null;
    }
    private async Task SetPriorityAsync(Guid id, int priority)
    {
        await _saveTask;
        if (_routine is null || _disposed || priority is < 1 or > 3) return;
        var activity = _routine.Activities.FirstOrDefault(a => a.Id == id);
        if (activity is null || activity.Deleted || activity.Priority == priority) return;
        activity.Priority = priority;
        await SaveAsync();
    }
    private async Task ToggleAsync(Guid id, DateOnly date)
    {
        if (_routine is null || _saving || _day != _today || date != _day) return;
        try { WeeklyRoutinePlanner.Toggle(_routine, id, date, _today); await SaveAsync(); }
        catch (ArgumentException error) { _error = error.Message; }
    }
    private async Task AddAsync()
    {
        if (_routine is null || _saving || !HasRoom || string.IsNullOrWhiteSpace(_newTitle) || _newPriority is < 1 or > 3) return;
        _routine.Activities.Add(new() { Title = _newTitle.Trim(), Priority = _newPriority });
        await SaveAsync(); if (_error is null) _newTitle = "";
    }
    private async Task ResetActivitiesAsync()
    {
        if (_routine is null || _saving || !_resetOpen) return;
        WeeklyRoutinePlanner.RestoreDefaultActivities(_routine);
        await SaveAsync();
        if (_error is null) { _resetOpen = false; _editingId = null; _deletedId = null; _editingPriorities.Clear(); }
    }
    private void MoveDay(int direction)
    {
        if (_saving || direction is not (-1 or 1) || direction < 0 && _day == DateOnly.MinValue || direction > 0 && _day >= _today) return;
        _day = _day.AddDays(direction);
    }
    private void UpdateDate(DateOnly today)
    {
        bool current = _day == _today;
        _today = today;
        if (current) _day = today;
    }
    private async Task WatchDateAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                var today = WeeklyRoutinePlanner.Today(DateTimeOffset.UtcNow);
                if (_disposed) return;
                if (today == _today) continue;
                await InvokeAsync(() => { UpdateDate(today); StateHasChanged(); });
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    public void Dispose() { _disposed = true; _stop.Cancel(); _stop.Dispose(); }
}
