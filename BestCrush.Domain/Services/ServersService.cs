using System.Net.Http.Headers;
using DofusSharp.Dofocus.ApiClients;
using DofusSharp.Dofocus.ApiClients.Models.Servers;

namespace BestCrush.Domain.Services;

public class ServersService(
    ImageCache imageCache,
    IDofocusClientFactory dofocusClientFactory,
    IWorkspacePreferences? workspacePreferences = null)
{
    public const string LastServerPreferenceKey = "Workspace.LastSelectedServer";

    private IReadOnlyCollection<DofocusServer>? _servers;
    private readonly SemaphoreSlim _serversLock = new(1, 1);
    private readonly SemaphoreSlim _selectionLock = new(1, 1);
    private DofocusServer? _currentServer;

    public async Task<IReadOnlyCollection<DofocusServer>> GetServers()
    {
        if (_servers is not null)
            return _servers;

        await _serversLock.WaitAsync();
        try
        {
            if (_servers is null)
            {
                IDofocusServersClient serversClient = dofocusClientFactory.Servers();
                _servers = await serversClient.GetServersAsync();
            }

            return _servers;
        }
        finally
        {
            _serversLock.Release();
        }
    }

    public async Task<string> GetServerIconAsync(DofocusServer server)
    {
        using HttpClient httpClient = new();
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Mozilla", "5.0"));
        string cacheKey = $"server-icon-{server.Name}.webp";
        byte[] content = await imageCache.GetOrAddAsync(cacheKey, async () => await httpClient.GetByteArrayAsync($"https://dofocus.fr/servers/{server.Name.ToLower()}.webp"));
        return $"image/webp;base64,{Convert.ToBase64String(content)}";
    }

    /// <summary>
    /// Only restore a server that is present in the current server catalog.
    /// Unknown old selections must never be used to enable network captures.
    /// </summary>
    public async Task<DofocusServer?> GetCurrentServer()
    {
        if (_currentServer is not null || workspacePreferences is null)
            return _currentServer;

        await _selectionLock.WaitAsync();
        try
        {
            if (_currentServer is not null)
                return _currentServer;

            string? savedName = workspacePreferences.GetString(LastServerPreferenceKey);
            if (string.IsNullOrWhiteSpace(savedName))
                return null;

            IReadOnlyCollection<DofocusServer> catalog = await GetServers();
            DofocusServer? restored = catalog.FirstOrDefault(server =>
                string.Equals(server.Name, savedName, StringComparison.OrdinalIgnoreCase));

            if (restored is null)
            {
                workspacePreferences.Remove(LastServerPreferenceKey);
                return null;
            }

            _currentServer = restored;
            return _currentServer;
        }
        finally
        {
            _selectionLock.Release();
        }
    }

    public async Task SetCurrentServerAsync(DofocusServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (string.IsNullOrWhiteSpace(server.Name))
            throw new ArgumentException("Le serveur doit avoir un nom.", nameof(server));

        await _selectionLock.WaitAsync();
        try
        {
            workspacePreferences?.SetString(LastServerPreferenceKey, server.Name);
            _currentServer = server;
        }
        finally
        {
            _selectionLock.Release();
        }
    }
}
