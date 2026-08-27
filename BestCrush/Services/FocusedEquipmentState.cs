using BestCrush.Domain.Models;

namespace BestCrush.Services;

public sealed class FocusedEquipmentState
{
    public Equipment? Equipment { get; private set; }

    public bool HasEquipment =>
        Equipment is not null;

    public void SetEquipment(
        Equipment equipment)
    {
        Equipment = equipment;
    }

    public void Clear()
    {
        Equipment = null;
    }
}