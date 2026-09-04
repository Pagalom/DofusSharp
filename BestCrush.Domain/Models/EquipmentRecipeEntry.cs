namespace BestCrush.Domain.Models;

public class EquipmentRecipeEntry
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.
    // EF ctor
    public EquipmentRecipeEntry() { }
#pragma warning restore CS8618

    public EquipmentRecipeEntry(
        Equipment equipment,
        Equipment ingredientEquipment,
        int count)
    {
        Equipment = equipment;
        IngredientEquipment = ingredientEquipment;
        Count = count;
    }

    public Guid Id { get; private set; }
    public Equipment Equipment { get; private set; }
    public Equipment IngredientEquipment { get; private set; }
    public int Count { get; set; }
}
