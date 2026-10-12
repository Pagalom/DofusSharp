namespace BestCrush.Services;

/// <summary>
/// A character is identified by a verified game ID on a particular server,
/// never by a remembered display name or the last selected server.
/// </summary>
public readonly record struct CharacterKey(string ServerName, ulong CharacterId);

/// <summary>
/// Item identifiers describe game data; InstanceUid distinguishes equipment
/// instances when the verified protocol exposes it.
/// </summary>
public sealed record CharacterInventoryItem(
    ulong ItemId,
    ulong? InstanceUid,
    long Quantity);

/// <summary>
/// Entirely in-process. Null means "not observed", not zero or empty.
/// Inventory and bank snapshots are independent.
/// </summary>
public sealed record CharacterSessionProfile(
    CharacterKey Key,
    string Name,
    int? ClassId,
    int? Level,
    long? PocketKamas = null,
    long? BankKamas = null,
    IReadOnlyList<CharacterInventoryItem>? Inventory = null,
    IReadOnlyList<CharacterInventoryItem>? BankInventory = null,
    IReadOnlyDictionary<int, int>? ProfessionLevels = null);

/// <summary>
/// Proposed verified semantic data, not yet connected to the network decoder.
/// A null field means "no new observation". A non-null empty list means that
/// an authoritative snapshot explicitly reported an empty inventory.
/// </summary>
internal sealed record CharacterDataObservation(
    CharacterConnectionScope Scope,
    ulong CharacterId,
    long? PocketKamas = null,
    long? BankKamas = null,
    IReadOnlyList<CharacterInventoryItem>? Inventory = null,
    IReadOnlyList<CharacterInventoryItem>? BankInventory = null,
    IReadOnlyDictionary<int, int>? ProfessionLevels = null);

/// <summary>
/// In-memory character workspace for the lifetime of the MAUI process only.
/// The profile list is created from confirmed identities and disappears on
/// application exit. The active character is NEVER restored on startup.
/// </summary>
public sealed class CurrentCharacterState
{
    private readonly object _sync = new();
    private readonly Dictionary<CharacterKey, CharacterSessionProfile> _profiles = [];
    private readonly Dictionary<CharacterKey, CharacterConnectionScope> _scopes = [];
    private readonly HashSet<CharacterConnectionScope> _revokedScopes = [];
    private string? _serverName;
    private CharacterKey? _active;

    public event Action? Changed;

    public string? ServerName
    {
        get { lock (_sync) return _serverName; }
    }

    public CharacterKey? ActiveCharacterKey
    {
        get { lock (_sync) return _active; }
    }

    public CharacterSessionProfile? ActiveProfile
    {
        get
        {
            lock (_sync)
                return _active is { } key &&
                    _profiles.TryGetValue(key, out CharacterSessionProfile? profile)
                    ? profile
                    : null;
        }
    }

    public string CharacterName => ActiveProfile?.Name ?? string.Empty;

    public IReadOnlyList<CharacterSessionProfile> Characters
    {
        get
        {
            lock (_sync)
                return _profiles.Values
                    .Where(profile =>
                        string.Equals(profile.Key.ServerName, _serverName,
                            StringComparison.OrdinalIgnoreCase))
                    .OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
        }
    }

    public void SelectServer(string? serverName)
    {
        lock (_sync)
        {
            if (string.Equals(_serverName, serverName, StringComparison.Ordinal))
                return;

            // Server restoration is independent of character detection.
            _serverName = serverName;
            _active = null;
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Called only with two independently confirmed network identity sources.
    /// If the focused Dofus game window is verified, prefer it automatically.
    /// Merely observing an unfocused client never steals the manual selection.
    /// </summary>
    internal bool ObserveConfirmedCharacter(
        CharacterIdentityAssessment identity,
        bool isVerifiedTargetWindow)
    {
        if (identity.Status != CharacterIdentityStatus.Confirmed ||
            identity.Character is not { CharacterId: > 0 } character ||
            string.IsNullOrWhiteSpace(identity.Scope.ServerName))
            return false;

        lock (_sync)
        {
            if (_revokedScopes.Contains(identity.Scope))
                return false;

            if (!string.Equals(_serverName, identity.Scope.ServerName,
                    StringComparison.OrdinalIgnoreCase))
                return false;

            CharacterKey key = new(identity.Scope.ServerName, character.CharacterId);

            if (_scopes.TryGetValue(key, out CharacterConnectionScope oldScope) &&
                oldScope != identity.Scope &&
                (identity.Scope.ServerGeneration < oldScope.ServerGeneration ||
                 (identity.Scope.ServerGeneration == oldScope.ServerGeneration &&
                  (identity.Scope.CaptureEpoch < oldScope.CaptureEpoch ||
                   (identity.Scope.CaptureEpoch == oldScope.CaptureEpoch &&
                    identity.Scope.ConnectionEpoch <= oldScope.ConnectionEpoch)))))

                return false;

            if (_profiles.TryGetValue(key, out CharacterSessionProfile? existing))
            {
                bool newConnection = !_scopes.TryGetValue(
                    key, out CharacterConnectionScope knownScope) || knownScope != identity.Scope;
                // On reconnect, never present old cash/inventory as current.
                _profiles[key] = existing with
                {
                    Name = character.Name,
                    ClassId = character.ClassId,
                    Level = character.Level,
                    PocketKamas = newConnection ? null : existing.PocketKamas,
                    BankKamas = newConnection ? null : existing.BankKamas,
                    Inventory = newConnection ? null : existing.Inventory,
                    BankInventory = newConnection ? null : existing.BankInventory,
                    ProfessionLevels = newConnection ? null : existing.ProfessionLevels
                };
            }
            else
            {
                _profiles.Add(key, new CharacterSessionProfile(
                    key, character.Name, character.ClassId, character.Level));
            }

            // A connection can change character without changing its TCP
            // endpoint. Never keep two characters marked as active on one
            // confirmed connection; discard the previous flow ownership.
            foreach (CharacterKey other in _scopes
                .Where(entry => entry.Key != key && entry.Value == identity.Scope)
                .Select(entry => entry.Key).ToArray())
            {
                _scopes.Remove(other);
                if (_active == other)
                    _active = null;
            }

            _scopes[key] = identity.Scope;
            if (isVerifiedTargetWindow)
                _active = key;
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// A manual click may select only an already discovered character.
    /// New character records cannot be created from a typed name.
    /// </summary>
    public bool IsCharacterConnected(CharacterKey key)
    {
        lock (_sync)
            return _scopes.ContainsKey(key);
    }

    public bool SelectCharacter(CharacterKey key)
    {
        lock (_sync)
        {
            if (!string.Equals(key.ServerName, _serverName,
                    StringComparison.OrdinalIgnoreCase) ||
                !_profiles.ContainsKey(key) ||
                !_scopes.ContainsKey(key))
                return false;

            _active = key;
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Only apply a validated observation to the *same* confirmed character
    /// and exact captured connection. Never attribute to the selected tab's
    /// character merely because it is active.
    /// </summary>
    internal bool ApplyObservedData(
        CharacterIdentityAssessment identity,
        CharacterDataObservation observation)
    {
        if (identity.Status != CharacterIdentityStatus.Confirmed ||
            identity.Character is not { CharacterId: > 0 } character ||
            observation.CharacterId != character.CharacterId ||
            identity.Scope != observation.Scope ||
            observation.PocketKamas is < 0 ||
            observation.BankKamas is < 0 ||
            observation.Inventory?.Any(item => item.ItemId == 0 || item.Quantity < 0) == true ||
            observation.BankInventory?.Any(item => item.ItemId == 0 || item.Quantity < 0) == true ||
            observation.ProfessionLevels?.Any(entry => entry.Key <= 0 || entry.Value <= 0) == true)
            return false;

        lock (_sync)
        {
            CharacterKey key = new(identity.Scope.ServerName, character.CharacterId);
            if (!_scopes.TryGetValue(key, out CharacterConnectionScope recorded) ||
                recorded != observation.Scope ||
                !_profiles.TryGetValue(key, out CharacterSessionProfile? existing))
                return false;

            _profiles[key] = existing with
            {
                PocketKamas = observation.PocketKamas ?? existing.PocketKamas,
                BankKamas = observation.BankKamas ?? existing.BankKamas,
                Inventory = observation.Inventory?.ToArray() ?? existing.Inventory,
                BankInventory = observation.BankInventory?.ToArray() ?? existing.BankInventory,
                ProfessionLevels = observation.ProfessionLevels is null
                    ? existing.ProfessionLevels
                    : new Dictionary<int, int>(observation.ProfessionLevels)
            };
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Disconnect removes the active selection, not other ephemeral cards.
    /// Revalidation after a new connection is required before new observations.
    /// </summary>
    internal void InvalidateConnection(CharacterConnectionScope scope)
    {
        bool changed = false;
        lock (_sync)
        {
            _revokedScopes.Add(scope);
            foreach (CharacterKey key in _scopes
                .Where(entry => entry.Value == scope)
                .Select(entry => entry.Key).ToArray())
            {
                _scopes.Remove(key);
                if (_active == key)
                {
                    _active = null;
                    changed = true;
                }
            }
        }

        if (changed)
            Changed?.Invoke();
    }

    public void ClearActiveCharacter()
    {
        lock (_sync)
        {
            if (_active is null)
                return;

            _active = null;
        }

        Changed?.Invoke();
    }
}
