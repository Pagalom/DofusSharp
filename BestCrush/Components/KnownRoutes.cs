using DofusSharp.Dofocus.ApiClients.Models.Servers;

namespace BestCrush.Components;

static class KnownRoutes
{
    public static string Splash() => "/";

    // M2: open the modular workspace, even without a selected server.
    public static string AfterInitialization(DofocusServer? currentServer) =>
        currentServer is null
            ? Module("concassage")
            : Server(currentServer);

    public static string Servers() => "/servers";
    public static string Server(string server) => $"/servers/{server}";
    public static string History(string server) => $"/servers/{server}/history";
    public static string Module(string slug) => $"/modules/{slug}";
    public static string Profile() => "/profile";
    public static string Settings(string server) => $"{Server(server)}?settings=1";
    public static string Server(DofocusServer server) => Server(server.Name);
}
