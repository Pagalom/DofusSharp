using System.ComponentModel.DataAnnotations;

namespace BestCrush.Domain.Models;

public class CrushHistorySession
{
#pragma warning disable CS8618
    public CrushHistorySession() { }
#pragma warning restore CS8618

    public CrushHistorySession(
        string serverName,
        DateTime startedAtUtc,
        DateTime completedAtUtc,
        double? totalValue)
    {
        ServerName = serverName;
        StartedAtUtc = startedAtUtc;
        CompletedAtUtc = completedAtUtc;
        TotalValue = totalValue;
    }

    public Guid Id { get; private set; }

    [MaxLength(64)]
    public string ServerName { get; private set; }

    public DateTime StartedAtUtc { get; private set; }
    public DateTime CompletedAtUtc { get; private set; }

    public double? TotalValue { get; private set; }

    public ICollection<CrushHistoryEquipment> Equipments { get; set; } = [];
    public ICollection<CrushHistoryRune> Runes { get; set; } = [];
}
