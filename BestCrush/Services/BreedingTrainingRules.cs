using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;

namespace BestCrush.Services;

internal static class BreedingTrainingRules
{
    internal static IReadOnlyList<PaddockGauge> Gauges { get; } =
        [PaddockGauge.Endurance, PaddockGauge.Maturity, PaddockGauge.Love, PaddockGauge.Experience];

    internal static string Name(PaddockGauge gauge) => gauge switch
    {
        PaddockGauge.SerenityGain => "Gain de sérénité",
        PaddockGauge.SerenityLoss => "Perte de sérénité",
        PaddockGauge.Endurance => "Endurance",
        PaddockGauge.Maturity => "Maturité",
        PaddockGauge.Love => "Amour",
        PaddockGauge.Experience => "Expérience",
        _ => throw new ArgumentOutOfRangeException(nameof(gauge))
    };

    internal static bool CanProgress(PaddockGauge gauge, int serenity) => gauge switch
    {
        PaddockGauge.Endurance => BreedingGaugeForecast.CanProgress(BreedingGauge.Endurance, serenity),
        PaddockGauge.Maturity => BreedingGaugeForecast.CanProgress(BreedingGauge.Maturity, serenity),
        PaddockGauge.Love => BreedingGaugeForecast.CanProgress(BreedingGauge.Love, serenity),
        PaddockGauge.Experience => true,
        PaddockGauge.SerenityGain => serenity < 5_000,
        PaddockGauge.SerenityLoss => serenity > -5_000,
        _ => false
    };

    internal static int? PointsNeeded(PaddockMountReading mount, PaddockGauge gauge, int experienceLevel,
        IReadOnlyDictionary<Guid, int>? resaleGoals = null)
    {
        if (gauge == PaddockGauge.Experience)
        {
            if (resaleGoals is not null && !resaleGoals.TryGetValue(mount.Id, out experienceLevel))
                return null;
            return mount.Level >= experienceLevel ? 0 : Math.Max(0, BreedingMountExperience.RequiredAtLevel(experienceLevel) - mount.Experience);
        }
        if (mount.Fertility == BreedingMountFertility.Sterile)
            return 0;

        int? points = gauge switch
        {
            PaddockGauge.Endurance => mount.Endurance,
            PaddockGauge.Maturity => mount.Maturity,
            PaddockGauge.Love => mount.Love,
            _ => null
        };
        return points.HasValue ? Math.Max(0, BreedingFuel.TrainingBarMaximum - points.Value) : null;
    }

    internal static int FuelNeeded(PaddockMountReading mount, PaddockGauge gauge, int points)
    {
        int multiplier = GainMultiplier(mount, gauge);
        return (points + multiplier - 1) / multiplier;
    }

    internal static int GainMultiplier(PaddockMountReading mount, PaddockGauge gauge) =>
        mount.TrainingBonuses.Contains(gauge) ? 2 : 1;

    internal static string DescribeProfile(PaddockMountReading? mount)
    {
        string? sex = mount?.Sex switch
        {
            BreedingMountSex.Female => "Femelle",
            BreedingMountSex.Male => "Mâle",
            _ => null
        };
        string? fertility = mount?.Fertility switch
        {
            BreedingMountFertility.Fertile => "Fertile",
            BreedingMountFertility.Ready => "Féconde",
            BreedingMountFertility.Sterile => "Stérile",
            _ => null
        };
        return string.Join(" · ", new[] { sex, fertility }.OfType<string>());
    }
}
