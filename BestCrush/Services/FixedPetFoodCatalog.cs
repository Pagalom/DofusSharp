using System.Collections.Frozen;
using System.Text.Json;
using BestCrush.Domain.Models;

namespace BestCrush.Services;

/// <summary>Shared, versioned base XP yields. Prices and stocks remain server-specific.</summary>
internal static class FixedPetFoodCatalog
{
    internal const string Version = "3.7.14.18-feeding-v1";
    internal const string Source = "Calcul client · 3.7.14.18";
    private sealed record Snapshot(string Version, string ClientVersion, PetFood[] Records);
    private static readonly Lazy<FrozenDictionary<long, PetFood>> Data = new(Read);
    internal static IReadOnlyDictionary<long, PetFood> All => Data.Value;
    internal static PetFood[] Foods { get; } = Data.Value.Values.Where(food => food.Experience > 0).ToArray();

    private static FrozenDictionary<long, PetFood> Read()
    {
        using var stream = typeof(FixedPetFoodCatalog).Assembly.GetManifestResourceStream("BestCrush.Data.pet-food-yields.json")
            ?? throw new InvalidDataException("Catalogue XP des aliments absent.");
        var snapshot = JsonSerializer.Deserialize<Snapshot>(stream)
            ?? throw new InvalidDataException("Catalogue XP des aliments invalide.");
        if (snapshot.Version != Version || snapshot.Records.Length == 0 ||
            snapshot.Records.Any(food => food.Id <= 0 || string.IsNullOrWhiteSpace(food.Name) ||
                !double.IsFinite(food.Experience) || food.Experience < 0 || food.MarketType is not (MarketObjectType.Resource or MarketObjectType.Rune)))
            throw new InvalidDataException("Catalogue XP des aliments invalide.");
        return snapshot.Records.ToFrozenDictionary(food => food.Id);
    }

    internal static PetFoodDefinition Definition(long id, string name = "", long? iconId = null)
    {
        All.TryGetValue(id, out var food);
        return new()
        {
            DofusDbId = id, Name = food?.Name ?? name, DofusDbIconId = food?.IconId ?? iconId,
            MarketType = food?.MarketType ?? MarketObjectType.Resource, Experience = food?.Experience,
            Craftable = food?.Craftable ?? false, ExperienceSource = food is null ? "" : Source
        };
    }
}
