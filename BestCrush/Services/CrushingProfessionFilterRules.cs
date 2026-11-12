namespace BestCrush.Services;

public static class CrushingProfessionFilterRules
{
    public static bool CanEnable(int? playerLevel, IReadOnlyDictionary<string, int>? professions, int? minimum, int? maximum) =>
        playerLevel is >= 1 and <= 200 && professions?.Any(x => x.Value > 0) == true && maximum is null &&
        (minimum is null || minimum >= 1 && minimum < playerLevel);

    public static string? UnavailableReason(int? playerLevel, IReadOnlyDictionary<string, int>? professions, int? minimum, int? maximum)
    {
        if (playerLevel is not (>= 1 and <= 200) || professions?.Any(x => x.Value > 0) != true)
            return "Un personnage, son niveau et ses niveaux de métier doivent être détectés sur ce serveur.";
        if (maximum is not null) return "Laissez le niveau maximum vide pour utiliser vos niveaux de métier.";
        if (minimum is not null && (minimum < 1 || minimum >= playerLevel))
            return "Le niveau minimum doit être strictement inférieur au niveau du personnage.";
        return null;
    }

    public static bool CanCraft(RecipeProfession? recipe, IReadOnlyDictionary<string, int>? professions) =>
        recipe is { Job: { } job, Level: > 0 } && professions is not null &&
        professions.Any(pair => string.Equals(pair.Key, job, StringComparison.OrdinalIgnoreCase) && pair.Value >= recipe.Level);
}
