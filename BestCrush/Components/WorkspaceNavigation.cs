using BestCrush.Domain.Models;

namespace BestCrush.Components;

public enum WorkspaceDataTab { Equipment, Runes, Resources, Coefficients, History }

public sealed record ObjectHistorySelection(MarketObjectType ObjectType, long ItemId, string Name);

/// <summary>In-memory navigation shared by the shell and its mounted activity.</summary>
public sealed class WorkspaceNavigation
{
    public bool DataOpen { get; private set; }
    public bool SettingsOpen { get; private set; }
    public WorkspaceDataTab DataTab { get; private set; } = WorkspaceDataTab.Equipment;
    public ObjectHistorySelection? HistorySelection { get; private set; }
    public event Action? Changed;
    public event Action? CrushSettingsChanged;

    public void OpenData() { DataOpen = true; Changed?.Invoke(); }
    public void CloseData() { DataOpen = false; Changed?.Invoke(); }
    public void SelectDataTab(WorkspaceDataTab tab) { DataTab = tab; Changed?.Invoke(); }
    public void ShowHistory(MarketObjectType type, long id, string name)
    {
        HistorySelection = new(type, id, name);
        DataTab = WorkspaceDataTab.History;
        DataOpen = true;
        Changed?.Invoke();
    }
    public void OpenSettings() { SettingsOpen = true; Changed?.Invoke(); }
    public void CloseSettings() { SettingsOpen = false; Changed?.Invoke(); }
    public void NotifyCrushSettingsChanged() => CrushSettingsChanged?.Invoke();
}
