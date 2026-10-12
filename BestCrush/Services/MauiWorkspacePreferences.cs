using BestCrush.Domain.Services;
using Microsoft.Maui.Storage;

namespace BestCrush.Services;

public sealed class MauiWorkspacePreferences : IWorkspacePreferences
{
    public string? GetString(string key) =>
        Preferences.Default.Get(key, string.Empty);

    public void SetString(string key, string value) =>
        Preferences.Default.Set(key, value);

    public void Remove(string key) =>
        Preferences.Default.Remove(key);
}
