using System.Text.RegularExpressions;

namespace BestCrush.Services;

/// <summary>A live local Dofus client window, not a verified game account.</summary>
public readonly record struct DofusClientWindowKey(long Handle, int ProcessId);

public sealed record DofusClientWindow(
    DofusClientWindowKey Key,
    string DisplayName,
    string? ClassName,
    string? ClientBuild,
    bool IsForeground)
{
    public bool HasCharacterTitle => ClassName is not null;
}

/// <summary>
/// Parses local Dofus window titles. Never use the result as a verified
/// game-account, server, character-ID or TCP-flow identity.
/// </summary>
public static partial class DofusWindowTitleParser
{
    [GeneratedRegex(
        @"^(?<name>.+) - (?<class>[^-]+) - (?<build>\d+\.\d+\.\d+\.\d+) - (?<channel>Release|Beta)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CharacterTitlePattern();

    public static (string? Name, string? ClassName, string? Build) Parse(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return (null, null, null);

        Match match = CharacterTitlePattern().Match(title.Trim());
        if (!match.Success)
            return (null, null, null);

        string name = match.Groups["name"].Value.Trim();
        string className = match.Groups["class"].Value.Trim();
        string build = match.Groups["build"].Value;
        if (name.Length is < 1 or > 80 || className.Length is < 1 or > 60)
            return (null, null, null);

        return (name, className, build);
    }
}

/// <summary>
/// RAM-only Windows client selector. UI selection never attributes network
/// messages, equipment, kamas or professions to a character or account.
/// </summary>
public sealed class CurrentDofusClientsState
{
    private readonly object _sync = new();
    private IReadOnlyList<DofusClientWindow> _clients = [];
    private DofusClientWindowKey? _selected;
    private DofusClientWindowKey? _lastDofusForeground;

    public event Action? Changed;

    public IReadOnlyList<DofusClientWindow> Clients
    {
        get { lock (_sync) return _clients; }
    }

    public DofusClientWindow? SelectedClient
    {
        get
        {
            lock (_sync)
                return _selected is { } key
                    ? _clients.FirstOrDefault(client => client.Key == key)
                    : null;
        }
    }

    public bool SelectWindow(DofusClientWindowKey key)
    {
        bool changed;
        lock (_sync)
        {
            if (!_clients.Any(client => client.Key == key))
                return false;

            changed = _selected != key;
            _selected = key;
            // A future Dofus activation supersedes a manual UI selection.
            _lastDofusForeground = null;
        }

        if (changed)
            Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Foreground affects UI targeting only, never ownership of incoming data.
    /// DofusClientWindowInfo values and account names are not persisted.
    /// </summary>
    public void Update(
        IReadOnlyList<DofusWindowInfo> windows,
        DofusClientWindowKey? foreground = null)
    {
        ArgumentNullException.ThrowIfNull(windows);
        bool changed;
        lock (_sync)
        {
            DofusClientWindowKey? previousSelected = _selected;
            List<DofusClientWindow> next = [];
            HashSet<DofusClientWindowKey> seen = [];
            foreach (DofusWindowInfo window in windows)
            {
                DofusClientWindowKey key =
                    new(window.Handle.ToInt64(), window.ProcessId);
                if (key.Handle == 0 || key.ProcessId <= 0 || !seen.Add(key))
                    continue;

                (string? name, string? className, string? build) =
                    DofusWindowTitleParser.Parse(window.Title);
                next.Add(new DofusClientWindow(
                    key, name ?? "Client Dofus", className, build,
                    foreground == key));
            }

            // Keep existing UI order even if Win32 Z-order changes with focus.
            Dictionary<DofusClientWindowKey, int> previousOrder = _clients
                .Select((client, index) => (client.Key, index))
                .ToDictionary(x => x.Key, x => x.index);
            next = next.OrderBy(client =>
                previousOrder.TryGetValue(client.Key, out int order)
                    ? order : int.MaxValue).ToList();

            if (_selected is { } selected && !seen.Contains(selected))
                _selected = null;

            if (foreground is { } focused && seen.Contains(focused))
            {
                if (_lastDofusForeground != focused)
                    _selected = focused;
                _lastDofusForeground = focused;
            }
            else if (_lastDofusForeground is { } prior && !seen.Contains(prior))
                _lastDofusForeground = null;

            if (_selected is null && next.Count == 1)
                _selected = next[0].Key;

            changed = previousSelected != _selected || !_clients.SequenceEqual(next);
            _clients = next.ToArray();
        }
        if (changed)
            Changed?.Invoke();
    }
}
