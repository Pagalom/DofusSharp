using System.ComponentModel.DataAnnotations;

namespace BestCrush.Domain.Models;

public class CrushHistoryEquipment
{
#pragma warning disable CS8618
    public CrushHistoryEquipment() { }
#pragma warning restore CS8618

    public CrushHistoryEquipment(
        CrushHistorySession session,
        long dofusDbId,
        long? dofusDbIconId,
        string equipmentName,
        double coefficientPercent,
        CoefficientSource coefficientSource,
        int rowY)
    {
        Session = session;
        DofusDbId = dofusDbId;
        DofusDbIconId = dofusDbIconId;
        EquipmentName = equipmentName;
        CoefficientPercent = coefficientPercent;
        CoefficientSource = coefficientSource;
        RowY = rowY;
    }

    public Guid Id { get; private set; }

    public Guid SessionId { get; private set; }
    public CrushHistorySession Session { get; private set; }

    public long DofusDbId { get; private set; }
    public long? DofusDbIconId { get; private set; }

    [MaxLength(256)]
    public string EquipmentName { get; private set; }

    public double CoefficientPercent { get; private set; }
    public CoefficientSource CoefficientSource { get; private set; }
    public int RowY { get; private set; }
}
