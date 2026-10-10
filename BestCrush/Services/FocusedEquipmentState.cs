using BestCrush.Domain.Models;

namespace BestCrush.Services;

public sealed class FocusedEquipmentState
{
    public Equipment? Equipment { get; private set; }

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