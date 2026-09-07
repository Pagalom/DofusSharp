using System.ComponentModel.DataAnnotations;

namespace BestCrush.Domain.Models;

public class CrushHistoryRune
{
#pragma warning disable CS8618
    public CrushHistoryRune() { }
#pragma warning restore CS8618

    public CrushHistoryRune(
        CrushHistorySession session,
        long dofusDbId,
        long? dofusDbIconId,
        string runeName,
        int quantity,
        double? value)
    {
        Session = session;
        DofusDbId = dofusDbId;
        DofusDbIconId = dofusDbIconId;
        RuneName = runeName;
        Quantity = quantity;
        Value = value;
    }

    public Guid Id { get; private set; }

    public Guid SessionId { get; private set; }
    public CrushHistorySession Session { get; private set; }

    public long DofusDbId { get; private set; }
    public long? DofusDbIconId { get; private set; }

    [MaxLength(256)]
    public string RuneName { get; private set; }

    public int Quantity { get; private set; }
    public double? Value { get; private set; }

    public ICollection<CrushHistoryRuneLot> Lots { get; set; } = [];
}
