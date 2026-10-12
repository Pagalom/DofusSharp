using DofusSharp.Dofocus.ApiClients.Models.Servers;

namespace BestCrush.Components;

static class KnownRoutes
{
    public static string Splash() => "/";

    // M0 characterization seam: Splash uses this exact legacy redirect.
    // M1/M2 will deliberately replace it with the modular landing route.
    public static string AfterInitialization(DofocusServer? currentServer) =>
        currentServer is null
            ? Servers()
            : Server(currentServer);

    public static string Servers() => "/servers";
    public static string Server(string server) => $"/servers/{server}";
    public static string History(string server) => $"/servers/{server}/history";
    public static string Server(DofocusServer server) => Server(server.Name);
}
