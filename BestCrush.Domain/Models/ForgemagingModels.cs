namespace BestCrush.Domain.Models;

public sealed class ForgemagingLine
{
    public Characteristic Characteristic { get; set; }
    public int? Minimum { get; set; }
    public int? Maximum { get; set; }
    public int? Current { get; set; }
    public int Target { get; set; }
    public bool IsNative => Minimum.HasValue && Maximum.HasValue;
    public ForgemagingLine Copy() => (ForgemagingLine)MemberwiseClone();
}

public sealed record ForgemagingRune(long Id, string Name, Characteristic Characteristic, int Gain, double? UnitPrice)
{
    public double Weight => Gain * Characteristic.GetWeight();
}

public sealed record ForgemagingOdds(double Critical, double Neutral, double Failure, string Regime, string? Blocked = null)
{
    public double Success => (Critical + Neutral) / 100;
}

public sealed record ForgemagingAdvice(ForgemagingRune Rune, ForgemagingOdds Odds, int UsefulGain,
    double? CostPerUsefulPoint, bool? SinkCovers, int Phase);

public enum ForgemagingOutcome { Critical, Neutral, Failure, Unconfirmed }

public sealed class ForgemagingAttempt
{
    public DateTime AtUtc { get; set; }
    public ForgemagingRune Rune { get; set; } = null!;
    public ForgemagingOutcome Outcome { get; set; }
    public bool FromGame { get; set; }
    public string? ObservationId { get; set; }
    public int RuneCount { get; set; } = 1;
    public ForgemagingOdds? PredictedOdds { get; set; }
    public int? GameResultCode { get; set; }
    public double? SinkBefore { get; set; }
    public double? SinkAfter { get; set; }
    public List<ForgemagingLine> Before { get; set; } = [];
    public List<ForgemagingLine> After { get; set; } = [];
}

public sealed class ForgemagingSession
{
    public long ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public int Level { get; set; }
    // An opaque recording id, never an account, HWND, PID or item instance UID.
    public string? GameRecordingId { get; set; }
    public List<ForgemagingLine> Lines { get; set; } = [];
    public double? Sink { get; set; }
    public double? Budget { get; set; }
    public List<ForgemagingAttempt> Attempts { get; set; } = [];
    public double KnownSpent => Attempts.Sum(a => (a.Rune.UnitPrice ?? 0) * a.RuneCount);
    public int UnpricedAttempts => Attempts.Count(a => a.Rune.UnitPrice is null);
}
