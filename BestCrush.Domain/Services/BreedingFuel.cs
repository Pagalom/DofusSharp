namespace BestCrush.Domain.Services;

public static class BreedingFuel
{
    public const int Capacity = 200_000;
    public const int TrainingBarMaximum = 20_000;

    public static int PointsPerCycle(int reserve) => reserve switch
    {
        <= 0 => 0,
        <= 80_000 => Math.Min(reserve, 10),
        <= 140_000 => 20,
        <= 180_000 => 30,
        _ => 40
    };

    public static TimeSpan? TimeToSpend(int reserve, int points)
    {
        if (points > reserve) return null;
        int cycles = 0;
        while (points > 0)
        {
            int gain = PointsPerCycle(reserve);
            int nextTier = reserve switch
            {
                > 180_000 => 180_000,
                > 140_000 => 140_000,
                > 80_000 => 80_000,
                _ => 0
            };
            int batch = Math.Min((reserve - nextTier - 1) / gain, (points - 1) / gain) + 1;
            int spent = (int)Math.Min(reserve, (long)batch * gain);
            reserve -= spent;
            points -= spent;
            cycles += batch;
        }
        return TimeSpan.FromSeconds(cycles * BreedingGaugeForecast.SecondsPerTick);
    }
}
