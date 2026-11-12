namespace BestCrush.Domain.Models;

public sealed class RoutineActivity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? DefaultKey { get; set; }
    public bool Deleted { get; set; }
    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public string? GuideUrl { get; set; }
    public string? ModuleSlug { get; set; }
    public int Priority { get; set; } = 2;
    public bool Enabled { get; set; } = true;
    public long? EstimatedNetKamas { get; set; }
}

public sealed record RoutineCompletion(Guid ActivityId, DateOnly Date);

// Legacy type/file name is kept so existing routine data remains readable. Version 2 is entirely daily.
public sealed class WeeklyRoutine
{
    public int Version { get; set; } = 2;
    public string Name { get; set; } = "Ma routine";
    public List<RoutineActivity> Activities { get; set; } = [];
    public List<RoutineCompletion> Completions { get; set; } = [];
}
