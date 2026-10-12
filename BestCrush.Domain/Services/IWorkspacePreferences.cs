namespace BestCrush.Domain.Services;

/// <summary>Minimal, MAUI-independent storage seam for workspace preferences.</summary>
public interface IWorkspacePreferences
{
    string? GetString(string key);
    void SetString(string key, string value);
    void Remove(string key);
}
