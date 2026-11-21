using BestCrush.Network.Protocol;
using BestCrush.Domain.Services;

namespace BestCrush.Services;

internal static class OverlayBreedingPresentation
{
    internal static bool IsOld(DateTime observed,DateTime now)=>now-observed>TimeSpan.FromMinutes(10);
    internal static string Countdown(DateTime deadline,DateTime now)
    {
        if(deadline<=now)return "0 h 00 min";
        long minutes=(long)Math.Ceiling((deadline-now).TotalMinutes);
        return $"{minutes/60} h {minutes%60:00} min";
    }

    internal static OverlayMountReading Mount(PaddockMountReading? mount,IReadOnlyDictionary<int,BreedingSpecies> species,
        DateTime? deadline=null,bool conditional=false)
    {
        var kind=mount?.SpeciesId is {} id?species.GetValueOrDefault(id):null;
        int? generation=kind?.Generation;
        if(generation is null && mount?.SpeciesId is {} rideId && BreedingLineage.Rides.TryGetValue(rideId,out var ride)) generation=ride.Generation;
        return new(kind?.Name??"Monture",mount,generation,deadline,conditional);
    }

    internal static OverlayActionCard Enclosure(LivePaddockReading reading,IReadOnlyDictionary<int,BreedingSpecies> species,
        IReadOnlyDictionary<Guid,int> goals,DateTime now)
    {
        var name=reading.Enclosure is {} number?$"Enclos {number}":"Enclos";
        var plans=reading.Reading.Gauges.Where(g=>g.Gauge is PaddockGauge.Maturity or PaddockGauge.Love or PaddockGauge.Endurance or PaddockGauge.Experience)
            .Select(g=>BreedingTrainingPlan.From(reading.Reading,g.Gauge,100,goals)).ToArray();
        var mounts=reading.Reading.Mounts.Select(m=>
        {
            var forecasts=plans.Select(p=>BreedingDashboardPresentation.Forecast(p,m.Id,reading.ObservedAtUtc)).Where(f=>f.Deadline is not null).ToArray();
            var next=forecasts.Where(f=>f.Deadline>now).MinBy(f=>f.Deadline)??forecasts.MaxBy(f=>f.Deadline);
            return Mount(m,species,next?.Deadline,next?.Conditional==true);
        }).ToArray();
        var plan=plans.FirstOrDefault(p=>p.Action!=TrainingAction.None);
        var notice=BreedingGaugeNotices.Build(name,reading.Reading,plans,goals,reading.ObservedAtUtc,now)
            .OrderBy(n=>n.Deadline??DateTime.MinValue).FirstOrDefault();
        string change=plan?.SerenityGauge==PaddockGauge.SerenityLoss?"la perte":"le gain";
        string? advice=plan?.Action switch
        {
            TrainingAction.PauseSerenity=>$"Couper {change} de sérénité",
            TrainingAction.KeepSerenity=>$"Maintenir {change} de sérénité",
            TrainingAction.ProtectMounts=>"Déplacer les montures à protéger",
            TrainingAction.ConfirmReadings=>"Vérifier les jauges manquantes",
            _=>null
        };
        advice=notice?.Title??advice;
        DateTime? deadline=notice?.Deadline??(plan?.Action switch
        {
            TrainingAction.KeepSerenity when plan.WaitingUntil is {} entry=>reading.ObservedAtUtc+entry,
            TrainingAction.ProtectMounts when plan.FirstRisk is {} risk=>reading.ObservedAtUtc+risk.NotifyAfter,
            _=>null
        });
        string? label=deadline is null?null:plan?.Action==TrainingAction.KeepSerenity?"Entrée dans la plage":"Intervenir dans";
        if(deadline is null)
        {
            var next=mounts.Where(m=>m.Deadline>now).MinBy(m=>m.Deadline)??mounts.Where(m=>m.Deadline is not null).MaxBy(m=>m.Deadline);
            deadline=next?.Deadline;label=deadline is null?null:next?.Conditional==true?"Prévision · sérénité stable":"Prochaine étape";
        }
        var gauges=reading.Reading.Gauges.Select(g=>new OverlayGauge(BreedingTrainingRules.Name(g.Gauge),g.Gauge switch {
            PaddockGauge.SerenityLoss=>"dash",PaddockGauge.SerenityGain=>"plus",PaddockGauge.Endurance=>"lightning-charge-fill",
            PaddockGauge.Maturity=>"droplet-fill",PaddockGauge.Love=>"heart-fill",_=>"stars"},g.Gauge switch {
            PaddockGauge.SerenityGain or PaddockGauge.SerenityLoss=>"serenity",PaddockGauge.Endurance=>"endurance",PaddockGauge.Maturity=>"maturity",
            PaddockGauge.Love=>"love",_=>"xp"},g.Fuel,BreedingFuel.Capacity,g.Active)).ToArray();
        return new(name,$"{mounts.Length} montures",[],[],advice)
        {Mounts=mounts,Gauges=gauges,OldReading=IsOld(reading.ObservedAtUtc,now),Deadline=deadline,TimerLabel=label};
    }
}
