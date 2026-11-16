namespace BestCrush.Services;

public enum EquipmentAcquisitionKind { Purchase, Craft }

public sealed record EquipmentAcquisition(EquipmentAcquisitionKind Kind, long? PricePaid, DateTime RecordedAtUtc, bool Manual = false);
