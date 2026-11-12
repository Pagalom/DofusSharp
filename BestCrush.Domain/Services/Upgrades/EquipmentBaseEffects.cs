using BestCrush.Domain.Models;

namespace BestCrush.Domain.Services.Upgrades;

internal static class EquipmentBaseEffects
{
    // The raw ranges are unsigned magnitudes. The base effect identifies a malus.
    public static (Characteristic Stat, int Sign)? Resolve(long? id)
    {
        Characteristic? malus = id switch
        {
            36 => Characteristic.Mp,
            37 => Characteristic.Ap,
            38 => Characteristic.Range,
            39 => Characteristic.Chance,
            40 => Characteristic.Intelligence,
            41 => Characteristic.Strength,
            42 => Characteristic.Wisdom,
            43 => Characteristic.Agility,
            44 => Characteristic.Vitality,
            75 => Characteristic.ResistancePercentEarth,
            76 => Characteristic.ResistancePercentWater,
            77 => Characteristic.ResistancePercentAir,
            78 => Characteristic.ResistancePercentFire,
            79 => Characteristic.ResistancePercentNeutral,
            98 => Characteristic.Critical,
            100 => Characteristic.Initiative,
            107 => Characteristic.Prospecting,
            114 => Characteristic.Healing,
            350 => Characteristic.ApDodge,
            352 => Characteristic.MpDodge,
            354 => Characteristic.ApReduction,
            356 => Characteristic.MpReduction,
            358 => Characteristic.DamageFlatPushback,
            360 => Characteristic.ResistanceFlatPushback,
            362 => Characteristic.DamageFlatCritical,
            364 => Characteristic.ResistanceFlatCritical,
            367 => Characteristic.DamageFlatEarth,
            369 => Characteristic.DamageFlatFire,
            371 => Characteristic.DamageFlatWater,
            373 => Characteristic.DamageFlatAir,
            375 => Characteristic.DamageFlatNeutral,
            377 => Characteristic.Dodge,
            378 => Characteristic.Lock,
            449 => Characteristic.ResistancePercentMelee,
            452 => Characteristic.DamagePercentDisance,
            453 => Characteristic.ResistancePercentDistance,
            460 => Characteristic.DamagePercentSpell,
            474 => Characteristic.Power,
            513 => Characteristic.Summons,
            543 => Characteristic.ResistanceFlatWater,
            _ => null
        };
        if (malus is { } negative) return (negative, -1);
        return RuneBaseEffects.Resolve(id) is { } positive ? (positive, 1) : null;
    }
}
