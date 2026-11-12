using BestCrush.Domain.Models;

namespace BestCrush.Domain.Services.Upgrades;

internal static class RuneBaseEffects
{
    // DofusDB possibleEffects.baseEffectId uses a different numbering from effectId.
    // These matches were verified against the same rune IDs in the rendered catalogue.
    public static Characteristic? Resolve(long? id) => id switch
    {
        2 => Characteristic.Ap,
        3 => Characteristic.Mp,
        4 => Characteristic.Strength,
        5 => Characteristic.Vitality,
        6 => Characteristic.Wisdom,
        7 => Characteristic.Intelligence,
        8 => Characteristic.Agility,
        9 => Characteristic.Chance,
        10 => Characteristic.Critical,
        11 => Characteristic.Range,
        12 => Characteristic.DamageFlatAll,
        22 => Characteristic.Power,
        23 => Characteristic.Summons,
        70 => Characteristic.ResistancePercentEarth,
        71 => Characteristic.ResistancePercentWater,
        72 => Characteristic.ResistancePercentAir,
        73 => Characteristic.ResistancePercentFire,
        74 => Characteristic.ResistancePercentNeutral,
        80 => Characteristic.Pods,
        99 => Characteristic.Initiative,
        106 => Characteristic.Prospecting,
        113 => Characteristic.Healing,
        116 => Characteristic.DamageReflection,
        130 => Characteristic.ResistanceFlatEarth,
        131 => Characteristic.ResistanceFlatWater,
        132 => Characteristic.ResistanceFlatAir,
        133 => Characteristic.ResistanceFlatFire,
        134 => Characteristic.ResistanceFlatNeutral,
        140 => Characteristic.DamageFlatTrap,
        141 => Characteristic.DamagePercentTrap,
        343 => Characteristic.Dodge,
        344 => Characteristic.Lock,
        349 => Characteristic.ApDodge,
        351 => Characteristic.MpDodge,
        353 => Characteristic.ApReduction,
        355 => Characteristic.MpReduction,
        357 => Characteristic.DamageFlatPushback,
        359 => Characteristic.ResistanceFlatPushback,
        361 => Characteristic.DamageFlatCritical,
        363 => Characteristic.ResistanceFlatCritical,
        365 => Characteristic.DamageFlatEarth,
        368 => Characteristic.DamageFlatFire,
        370 => Characteristic.DamageFlatWater,
        372 => Characteristic.DamageFlatAir,
        374 => Characteristic.DamageFlatNeutral,
        447 => Characteristic.DamagePercentMelee,
        450 => Characteristic.ResistancePercentMelee,
        451 => Characteristic.DamagePercentDisance,
        454 => Characteristic.ResistancePercentDistance,
        455 => Characteristic.DamagePercentWeapon,
        459 => Characteristic.DamagePercentSpell,
        _ => null
    };
}
