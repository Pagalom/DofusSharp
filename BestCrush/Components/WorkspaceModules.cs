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

    internal static string Href(WorkspaceModule module, string serverName) =>
        module.Slug == "concassage"
            ? KnownRoutes.Server(serverName)
            : KnownRoutes.Module(module.Slug);

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

        if (segments.Length >= 2 &&
            segments[0] == "servers" &&
            !string.IsNullOrWhiteSpace(segments[1]))
        {
            return "concassage";
        }

        return null;
    }
}
