using BestCrush.Domain.Models;

namespace BestCrush.Domain.Services;

public static class WeeklyRoutinePlanner
{
    private const string SoulActivityKey = "capture-souls";
    private static readonly Guid SoulActivityId = new("6ce9aaad-7c93-4cdc-894b-a55beca4450c");

    public static void AddMissingSoulActivity(WeeklyRoutine routine)
    {
        if (routine.Activities.Count < 10000 && routine.Activities.Count(a => !a.Deleted) < 200 &&
            !routine.Activities.Any(a => a.DefaultKey == SoulActivityKey || a.Id == SoulActivityId))
            routine.Activities.Add(CreateSoulActivity());
    }

    private static RoutineActivity CreateSoulActivity() => new()
    {
        Id = SoulActivityId, DefaultKey = SoulActivityKey, Title = "Capturer des âmes de boss",
        Priority = 2, ModuleSlug = "pierres-ame",
        Notes = "Comparez le coût de la pierre vide, le gain estimé et le nombre de pierres conseillé à la revente."
    };

    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")).DateTime);
    public static bool Eligible(WeeklyRoutine routine, RoutineActivity activity) => activity.Enabled && !activity.Deleted;
    public static bool DoneOn(WeeklyRoutine routine, RoutineActivity activity, DateOnly day) =>
        routine.Completions.Any(completion => completion.ActivityId == activity.Id && completion.Date == day);
    public static RoutineActivity? Next(WeeklyRoutine routine, DateOnly today) => routine.Activities
        .Where(a => Eligible(routine, a) && !DoneOn(routine, a, today)).OrderBy(a => a.Priority)
        .FirstOrDefault();

    public static void Toggle(WeeklyRoutine routine, Guid activityId, DateOnly date, DateOnly today)
    {
        var activity = routine.Activities.Single(a => a.Id == activityId);
        if (date > today) throw new ArgumentException("Une activité future ne peut pas être validée.");
        if (!Eligible(routine, activity)) throw new ArgumentException("Cette activité est mise de côté ou supprimée.");
        var matches = routine.Completions.Where(completion => completion.ActivityId == activityId && completion.Date == date).ToArray();
        if (matches.Length > 0) routine.Completions.RemoveAll(c => matches.Contains(c));
        else routine.Completions.Add(new(activityId, date));
    }

    public static void RestoreDefaultActivities(WeeklyRoutine routine)
    {
        AssignDefaultKeys(routine);
        var remaining = routine.Activities.ToList();
        var restored = CreateDefault().Activities;
        foreach (var activity in restored)
        {
            // Stable keys preserve the original identity after renaming or deletion.
            var previous = remaining.FirstOrDefault(a => a.DefaultKey == activity.DefaultKey);
            if (previous is null) continue;
            activity.Id = previous.Id;
            remaining.Remove(previous);
        }
        // Keep custom objectives and their IDs so their historical completions remain readable.
        foreach (var activity in remaining) activity.Enabled = false;
        routine.Activities = [.. restored, .. remaining];
    }

    public static void AssignDefaultKeys(WeeklyRoutine routine)
    {
        foreach (var template in CreateDefault().Activities)
        {
            if (routine.Activities.Any(a => a.DefaultKey == template.DefaultKey)) continue;
            var legacy = routine.Activities.FirstOrDefault(a => a.DefaultKey is null && a.Title == template.Title && a.ModuleSlug == template.ModuleSlug);
            if (legacy is not null) legacy.DefaultKey = template.DefaultKey;
        }
    }

    public static void Validate(WeeklyRoutine routine)
    {
        if (routine.Version != 2 || routine.Name.Length is < 1 or > 80 ||
            routine.Activities.Count > 10000 || routine.Activities.Count(a => !a.Deleted) > 200 || routine.Completions.Count > 100000)
            throw new ArgumentException("Paramètres de routine invalides.");
        if (routine.Activities.Select(a => a.Id).Distinct().Count() != routine.Activities.Count || routine.Activities.Any(a =>
            a.Id == Guid.Empty || a.DefaultKey is { Length: > 60 } || string.IsNullOrWhiteSpace(a.Title) || a.Title.Length > 120 || a.Notes.Length > 1500 ||
            a.Priority is < 1 or > 3 || a.EstimatedNetKamas is < -1000000000 or > 1000000000 ||
            a.ModuleSlug is { } module && module is not ("achat-vente" or "craft" or "farming" or "concassage" or "forgemagie" or "opportunites" or "pierres-ame") ||
            a.GuideUrl is { } url && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")))
            throw new ArgumentException("Une activité contient une valeur invalide.");
        var templateKeys = routine.Activities.Where(a => a.DefaultKey is not null).Select(a => a.DefaultKey).ToArray();
        if (templateKeys.Distinct().Count() != templateKeys.Length) throw new ArgumentException("Activités d’origine dupliquées.");
        if (routine.Completions.Any(c => routine.Activities.All(a => a.Id != c.ActivityId)) || routine.Completions.Distinct().Count() != routine.Completions.Count)
            throw new ArgumentException("Historique de routine invalide.");
    }

    public static WeeklyRoutine CreateDefault() => new()
    {
        Activities =
        [
            new() { DefaultKey = "sell-resources", Title = "Valoriser les ressources accumulées", Priority = 1, ModuleSlug = "achat-vente",
                Notes = "Partez des stocks synchronisés et des prix de votre serveur. Comparez la vente des ressources aux crafts qui les utilisent avant de choisir quoi vendre." },
            new() { DefaultKey = "guild-missions", Title = "Vérifier les missions de guilde", Priority = 1, ModuleSlug = "farming",
                Notes = "Consultez les missions dans Dofus et retenez celles compatibles avec vos récoltes ou combats prévus. La validation et les récompenses restent à vérifier en jeu." },
            new() { DefaultKey = "guildatons", Title = "Comparer les échanges de Guildatons", Priority = 1, ModuleSlug = "craft",
                Notes = "Vérifiez en jeu votre solde, les galets disponibles et les quotas de la semaine. Comparez leur prix sur le serveur et leur utilité dans vos crafts. Une économie de craft ne constitue pas une vente encaissée." },
            new() { DefaultKey = "gather-or-craft", Title = "Choisir une récolte ou un craft intéressant", Priority = 2, ModuleSlug = "farming",
                Notes = "Consultez Farming et Craft avec vos niveaux de métier et les prix connus. Choisissez un objectif adapté à votre personnage ; vérifiez la demande et les informations manquantes." },
            new() { DefaultKey = "crush-fm", Title = "Examiner les opportunités de concassage et de FM", Priority = 2, ModuleSlug = "concassage",
                Notes = "Comparez les possibilités avec vos stocks et le coefficient relevé. Pour préparer un objet, utilisez le conseiller FM ; comptez les runes consommées dans votre marge." },
            CreateSoulActivity(),
            new() { DefaultKey = "sell-listings", Title = "Mettre en vente et contrôler les invendus", Priority = 3, ModuleSlug = "achat-vente",
                Notes = "Effectuez vos ventes dans Dofus, puis vérifiez les lots invendus. Renseignez ici une estimation nette après coûts ; les coches ne prouvent pas qu’une vente a eu lieu." }
        ]
    };
}
