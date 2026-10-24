namespace BestCrush.Components;

/// <summary>
/// The nine fixed workspace tabs. The current Concassage UI keeps its
/// legacy route until M3; the new modules use server-independent routes.
/// </summary>
internal sealed record WorkspaceModule(string Slug, string Name);

internal static class WorkspaceModules
{
    internal static IReadOnlyList<WorkspaceModule> All { get; } =
    [
        new("concassage", "Concassage"),
        new("achat-vente", "Achat / Vente"),
        new("pierres-ame", "Pierres d'âme"),
        new("craft", "Craft"),
        new("familiers-xp", "Familiers / XP"),
        new("elevage", "Élevage"),
        new("farming", "Farming"),
        new("forgemagie", "Forgemagie"),
        new("opportunites", "Opportunités")
    ];

    internal static WorkspaceModule? Find(string? slug) =>
        All.FirstOrDefault(module =>
            string.Equals(module.Slug, slug, StringComparison.Ordinal));

    internal static string Href(WorkspaceModule module, string? serverName) =>
        module.Slug == "concassage" && !string.IsNullOrWhiteSpace(serverName)
            ? KnownRoutes.Server(serverName)
            : KnownRoutes.Module(module.Slug);

    // Extract legacy route scope before rendering any server-scoped component.
    internal static string? ServerFromRoute(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        string path = relativePath.Split('?', '#')[0].Trim('/');
        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return segments.Length >= 2 && segments[0] == "servers"
            ? Uri.UnescapeDataString(segments[1])
            : null;
    }

    internal static bool IsRouteCompatible(string? relativePath, string? selectedServer) =>
        ServerFromRoute(relativePath) is not { } fromUrl ||
        (selectedServer is not null &&
         string.Equals(fromUrl, selectedServer, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Change only the SQL-backed server scope in a legacy page URL while
    /// preserving the current page (prices/history/coefficients) and query.
    /// No routing decision ever changes the server of a Dofus game flow.
    /// </summary>
    internal static string? ReplaceServerInRoute(
        string? relativePath, string? serverName)
    {
        if (ServerFromRoute(relativePath) is null)
            return null;

        if (string.IsNullOrWhiteSpace(serverName))
            return KnownRoutes.Module("concassage");

        string path = relativePath!.TrimStart('/');
        int queryStart = path.IndexOfAny(['?', '#']);
        string suffix = queryStart < 0 ? string.Empty : path[queryStart..];
        string core = queryStart < 0 ? path : path[..queryStart];
        string[] segments = core.Split('/');

        if (segments.Length < 2 || segments[0] != "servers")
            return null;

        segments[1] = Uri.EscapeDataString(serverName);
        return "/" + string.Join("/", segments) + suffix;
    }

    /// <summary>
    /// Only actual workspace modules count as tabs. The existing
    /// /servers/{name}/... subpages still belong to Concassage.
    /// </summary>
    internal static string? ActiveSlug(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return null;

        string path = relativePath.Split('?', '#')[0].Trim('/');
        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 2 &&
            segments[0] == "modules")
        {
            return Find(segments[1])?.Slug;
        }

        if (ServerFromRoute(relativePath) is not null)
        {
            return "concassage";
        }

        return null;
    }
}
