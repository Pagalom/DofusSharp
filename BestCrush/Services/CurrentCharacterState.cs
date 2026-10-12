using BestCrush.Domain.Services;

namespace BestCrush.Services;

/// <summary>
/// One selected character name per server, shared across workspace modules.
/// Future profession fields will be modelled when their business semantics are defined.
/// </summary>
public sealed class CurrentCharacterState(IWorkspacePreferences preferences)
{
    private const string CharacterKeyPrefix = "Workspace.CharacterName.";
    private readonly object _sync = new();
    private string? _serverName;
    private string _characterName = string.Empty;

    public event Action? Changed;

    public string? ServerName
    {
        get { lock (_sync) return _serverName; }
    }

    public string CharacterName
    {
        get { lock (_sync) return _characterName; }
    }

    public void SelectServer(string? serverName)
    {
        lock (_sync)
        {
            if (string.Equals(_serverName, serverName, StringComparison.Ordinal))
                return;

            string name = string.IsNullOrWhiteSpace(serverName)
                ? string.Empty
                : preferences.GetString(CharacterKeyPrefix + serverName)?.Trim() ?? string.Empty;

            _serverName = serverName;
            _characterName = name;
        }

        Changed?.Invoke();
    }

    public void SetCharacterName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string trimmed = name.Trim();
        if (trimmed.Length > 80)
            throw new ArgumentOutOfRangeException(nameof(name), "80 caractères maximum.");

        lock (_sync)
        {
            if (_serverName is null)
                throw new InvalidOperationException("Sélectionnez d'abord un serveur.");

            preferences.SetString(CharacterKeyPrefix + _serverName, trimmed);
            _characterName = trimmed;
        }

        Changed?.Invoke();
    }
}
