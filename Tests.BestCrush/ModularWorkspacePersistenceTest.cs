using BestCrush.Components;
using BestCrush.Domain.Services;
using BestCrush.Services;
using DofusSharp.Dofocus.ApiClients;
using DofusSharp.Dofocus.ApiClients.Models.Servers;
using FluentAssertions;
using Moq;

namespace Tests.BestCrush;

public sealed class ModularWorkspacePersistenceTest
{
    [Fact]
    public async Task LastServerIsPersistedAndRestoredAcrossServiceInstances()
    {
        FakePreferences preferences = new();
        ServersService first = CreateService(preferences, "Kourial", "Other");
        await first.SetCurrentServerAsync(new DofocusServer { Name = "Kourial" });

        preferences.GetString(ServersService.LastServerPreferenceKey)
            .Should().Be("Kourial");

        ServersService second = CreateService(preferences, "Kourial", "Other");
        (await second.GetCurrentServer())!.Name.Should().Be("Kourial");
    }

    [Fact]
    public async Task MissingLastServerLeavesCaptureContextUnselected()
    {
        FakePreferences preferences = new();
        ServersService service = CreateService(preferences, "Kourial");
        (await service.GetCurrentServer()).Should().BeNull();

        CurrentServerState capture = new();
        capture.HasSelectedServer.Should().BeFalse();
        capture.GetCaptureLease().Should().BeNull();
        KnownRoutes.AfterInitialization(null).Should().Be("/modules/concassage");
    }

    [Fact]
    public async Task RemovedServerIsNotRestoredAndStalePreferenceIsCleared()
    {
        FakePreferences preferences = new();
        preferences.SetString(ServersService.LastServerPreferenceKey, "DeletedServer");
        ServersService service = CreateService(preferences, "Kourial");

        (await service.GetCurrentServer()).Should().BeNull();
        preferences.GetString(ServersService.LastServerPreferenceKey).Should().BeNull();
    }

    [Fact]
    public async Task RestoredServerReusesCanonicalCatalogName()
    {
        FakePreferences preferences = new();
        preferences.SetString(ServersService.LastServerPreferenceKey, "kourial");
        ServersService service = CreateService(preferences, "Kourial");

        (await service.GetCurrentServer())!.Name.Should().Be("Kourial");
    }

    [Fact]
    public async Task RecreatedCaptureContextRejectsOldServerLeaseOnSwitch()
    {
        FakePreferences preferences = new();
        ServersService service = CreateService(preferences, "Kourial", "Other");
        CurrentServerState capture = new();

        await service.SetCurrentServerAsync(new DofocusServer { Name = "Kourial" });
        capture.SelectServer((await service.GetCurrentServer())!.Name);
        NetworkCaptureLease first = capture.GetCaptureLease()!.Value;

        await service.SetCurrentServerAsync(new DofocusServer { Name = "Other" });
        capture.SelectServer((await service.GetCurrentServer())!.Name);

        capture.IsCaptureLeaseActive(first).Should().BeFalse();
        capture.GetCaptureLease()!.Value.ServerName.Should().Be("Other");
        preferences.GetString(ServersService.LastServerPreferenceKey).Should().Be("Other");
    }

    [Theory]
    [InlineData("servers/Kourial", "Kourial")]
    [InlineData("/servers/Kourial/history?lot=3", "Kourial")]
    [InlineData("servers/Other/rune-prices", "Other")]
    public void LegacyRoutesExposeTheirServerScope(string route, string expected)
    {
        WorkspaceModules.ServerFromRoute(route).Should().Be(expected);
    }

    [Theory]
    [InlineData("servers/Kourial", "Other", false)]
    [InlineData("servers/Kourial/history", "Other", false)]
    [InlineData("servers/Kourial", null, false)]
    [InlineData("servers/Kourial", "Kourial", true)]
    [InlineData("servers/Kourial", "kourial", true)]
    [InlineData("modules/craft", null, true)]
    [InlineData("profile", "Other", true)]
    public void ServerScopedPagesOnlyRenderWithMatchingGlobalSelection(
        string route, string? currentServer, bool expected)
    {
        WorkspaceModules.IsRouteCompatible(route, currentServer)
            .Should().Be(expected);
    }

    [Fact]
    public void ConcassageWithoutServerUsesModularLandingPage()
    {
        WorkspaceModule concassage = WorkspaceModules.Find("concassage")!;
        WorkspaceModules.Href(concassage, null).Should().Be("/modules/concassage");
        WorkspaceModules.Href(concassage, "Kourial").Should().Be("/servers/Kourial");
        WorkspaceModules.ActiveSlug("modules/concassage").Should().Be("concassage");
    }

    [Fact]
    public void CharacterProfileIsPersistedPerServerAndSharedInMemory()
    {
        FakePreferences preferences = new();
        CurrentCharacterState profile = new(preferences);
        int changes = 0;
        profile.Changed += () => changes++;

        profile.SelectServer("Kourial");
        profile.SetCharacterName(" Alice ");
        profile.CharacterName.Should().Be("Alice");

        profile.SelectServer("Other");
        profile.CharacterName.Should().BeEmpty();
        profile.SetCharacterName("Bob");

        CurrentCharacterState recreated = new(preferences);
        recreated.SelectServer("Kourial");
        recreated.CharacterName.Should().Be("Alice");
        recreated.SelectServer("Other");
        recreated.CharacterName.Should().Be("Bob");
        changes.Should().Be(4);
    }

    [Fact]
    public void NoServerDisallowsCharacterWrites()
    {
        CurrentCharacterState profile = new(new FakePreferences());
        Action save = () => profile.SetCharacterName("Alice");
        save.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CharacterNameRejectsOversizedInput()
    {
        CurrentCharacterState profile = new(new FakePreferences());
        profile.SelectServer("Kourial");
        Action save = () => profile.SetCharacterName(new string('A', 81));
        save.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static ServersService CreateService(
        IWorkspacePreferences preferences, params string[] serverNames)
    {
        IReadOnlyCollection<DofocusServer> servers = serverNames
            .Select(name => new DofocusServer { Name = name })
            .ToArray();

        Mock<IDofocusServersClient> serversClient = new();
        serversClient.Setup(client =>
            client.GetServersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(servers);

        Mock<IDofocusClientFactory> factory = new();
        factory.Setup(client => client.Servers()).Returns(serversClient.Object);

        return new ServersService(null!, factory.Object, preferences);
    }

    private sealed class FakePreferences : IWorkspacePreferences
    {
        private readonly Dictionary<string, string> _values = new();

        public string? GetString(string key) =>
            _values.TryGetValue(key, out string? value) ? value : null;

        public void SetString(string key, string value) => _values[key] = value;
        public void Remove(string key) => _values.Remove(key);
    }
}
