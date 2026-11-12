using BestCrush.Domain.Models;
namespace BestCrush.Components.Pages;
public partial class ForgemagingWorkspace
{
    private static string StatName(Characteristic stat) => stat switch
    {
        Characteristic.Wisdom=>"Sagesse",Characteristic.Strength=>"Force",Characteristic.Intelligence=>"Intelligence",Characteristic.Chance=>"Chance",Characteristic.Agility=>"Agilité",
        Characteristic.Vitality=>"Vitalité",Characteristic.Initiative=>"Initiative",Characteristic.Prospecting=>"Prospection",Characteristic.Power=>"Puissance",Characteristic.Healing=>"Soins",
        Characteristic.Ap=>"PA",Characteristic.Mp=>"PM",Characteristic.Range=>"Portée",Characteristic.Summons=>"Invocations",Characteristic.Critical=>"Critiques",Characteristic.Pods=>"Pods",
        Characteristic.Dodge=>"Fuite",Characteristic.Lock=>"Tacle",Characteristic.ApDodge=>"Esquive PA",Characteristic.MpDodge=>"Esquive PM",Characteristic.ApReduction=>"Retrait PA",Characteristic.MpReduction=>"Retrait PM",Characteristic.Hunting=>"Chasse",Characteristic.DamageReflection=>"Renvoi de dommages",
        Characteristic.DamageFlatAll=>"Dommages",Characteristic.DamageFlatNeutral=>"Dommages neutres",Characteristic.DamageFlatEarth=>"Dommages terre",Characteristic.DamageFlatFire=>"Dommages feu",Characteristic.DamageFlatWater=>"Dommages eau",Characteristic.DamageFlatAir=>"Dommages air",
        Characteristic.DamageFlatCritical=>"Dommages critiques",Characteristic.DamageFlatPushback=>"Dommages de poussée",Characteristic.DamageFlatTrap=>"Dommages aux pièges",Characteristic.DamagePercentTrap=>"Dommages aux pièges (%)",
        Characteristic.DamagePercentSpell=>"Dommages aux sorts (%)",Characteristic.DamagePercentWeapon=>"Dommages aux armes (%)",Characteristic.DamagePercentDisance=>"Dommages à distance (%)",Characteristic.DamagePercentMelee=>"Dommages en mêlée (%)",
        Characteristic.ResistanceFlatNeutral=>"Résistance neutre",Characteristic.ResistanceFlatEarth=>"Résistance terre",Characteristic.ResistanceFlatFire=>"Résistance feu",Characteristic.ResistanceFlatWater=>"Résistance eau",Characteristic.ResistanceFlatAir=>"Résistance air",
        Characteristic.ResistanceFlatCritical=>"Résistance critique",Characteristic.ResistanceFlatPushback=>"Résistance poussée",Characteristic.ResistancePercentNeutral=>"Résistance neutre (%)",Characteristic.ResistancePercentEarth=>"Résistance terre (%)",Characteristic.ResistancePercentFire=>"Résistance feu (%)",Characteristic.ResistancePercentWater=>"Résistance eau (%)",Characteristic.ResistancePercentAir=>"Résistance air (%)",
        Characteristic.ResistancePercentDistance=>"Résistance à distance (%)",Characteristic.ResistancePercentMelee=>"Résistance en mêlée (%)",
        _=>stat.ToDisplayName()
    };
}
