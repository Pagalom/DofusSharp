using System.Text.Json;

namespace BestCrush.Services;

internal sealed record BreedingRide(int Id, int Family, int Generation, long ItemId, int[][] Parents);

internal static class BreedingLineage
{
    private sealed record LineageSource(BreedingRide[] Rides);
    private static readonly Lazy<IReadOnlyDictionary<int, BreedingRide>> Catalog = new(() =>
    {
        using var data = typeof(BreedingLineage).Assembly.GetManifestResourceStream("BestCrush.BreedingLineage")
            ?? throw new InvalidDataException("Table des croisements indisponible.");
        return JsonSerializer.Deserialize<LineageSource>(data, JsonSerializerOptions.Web)!.Rides.ToDictionary(ride => ride.Id);
    });

    internal static IReadOnlyDictionary<int, BreedingRide> Rides => Catalog.Value;

    private static readonly Lazy<ILookup<(int, int), BreedingRide>> Crosses = new(() =>
        Rides.Values.SelectMany(ride => ride.Parents.Select(parents => (Parents: (parents[0], parents[1]), Ride: ride)))
            .ToLookup(entry => entry.Parents, entry => entry.Ride));

    internal static IEnumerable<BreedingRide> DirectCrosses(int first, int second) =>
        Crosses.Value[(Math.Min(first, second), Math.Max(first, second))];

    internal static string MarketName(string name) => name.EndsWith(" Sauvage", StringComparison.OrdinalIgnoreCase)
        ? name[..^" Sauvage".Length] : name;
}
