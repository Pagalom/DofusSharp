using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BestCrush.Services;

public sealed partial class OverlayWorkspaceService
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<WorkspaceOverlayKind,OverlayModuleView> _modules=new();
    private PetOpportunityReport? _pets;
    public PetOpportunityReport? PetReport => _pets;
    public bool PetAnalysisLoading => Volatile.Read(ref _petLoading)!=0;
    public bool PetServerKnown => ActiveGameServer is not null;
    public string? PetError {get;private set;}
    private long _petRevision;
    private DateTime _petRetryAfter;
    private DateTime _petLoadedAt;
    private string? _breedingSignature, _fmSignature;
    private int _pollTicks;
    private bool _petsRequested;
    private volatile bool _petDirty=true;
    private volatile bool _ownedStockDirty;
    private int _ownedRefreshing;
    private string? _ownedStockSignature;
    private void StockInputsChanged(){PetInputsChanged();_ownedStockDirty=true;Notify();}
    private async Task RefreshOwnedFocusAsync()
    {
        if(Interlocked.Exchange(ref _ownedRefreshing,1)!=0)return;
        _ownedStockDirty=false;
        try
        {
            var key=_instanceIdentity;var item=_item;var server=_server;var window=TargetWindow;var intent=FocusVersion;
            if(key is null || item is null || server is null || window is null || stock.ForWindow(window.Value) is not { } source)return;
            var entry=source.Containers.Where(c=>c.Complete && !c.Stale && c.Container is "inventory" or "bank")
                .SelectMany(c=>c.Items).FirstOrDefault(i=>i.ItemId==(ulong)item.DofusDbId && ForgemagedEquipmentStock.InstanceKey(server,item.DofusDbId,i.Uid)==key);
            if(entry is null)return;
            var signature=$"{key}:{entry.EffectIssue}:"+(entry.EffectWire is { } raw
                ? string.Join(';',raw.Select(Convert.ToHexString))
                : string.Join(';',entry.Effects?.Select(e=>$"{e.EffectCode}:{e.Value}") ?? []));
            if(signature==_ownedStockSignature)return;
            using var scope=scopes.CreateScope();var s=scope.ServiceProvider;
            var rows=await ForgemagedEquipmentStock.LoadAsync(source,server,s.GetRequiredService<ForgemagingSessionStore>(),
                s.GetRequiredService<ForgemagingDataService>(),s.GetRequiredService<ActivityCatalogService>(),
                s.GetRequiredService<CoefficientService>(),s.GetRequiredService<IBestCrushSettingsProvider>(),true,_stop.Token,item.DofusDbId);
            if(intent!=FocusVersion || TargetWindow!=window || _instanceIdentity!=key)return;
            if(rows.FirstOrDefault(r=>r.Key==key) is {Roll:not null} owned)
            {_ownedStockSignature=signature;await FocusOwnedAsync(owned,server);}
        }
        catch(Exception ex) when(!_stop.IsCancellationRequested){logger.LogDebug(ex,"Owned overlay roll refresh unavailable");}
        finally{Interlocked.Exchange(ref _ownedRefreshing,0);}
    }
    private void PetInputsChanged(){Interlocked.Increment(ref _petRevision);_petDirty=true;_petLoadedAt=default;_petRetryAfter=default;}
    public void OverlayVisibilityChanged(WorkspaceOverlayKind kind,bool visible){if(kind==WorkspaceOverlayKind.Pets)_petsRequested=visible;if(kind==WorkspaceOverlayKind.Runes)_runesRequested=visible;}
    private bool _fmPrepareCrushing=true;
    public void SetFmIntent(bool crushing)
    {if(_fmPrepareCrushing==crushing)return;_fmPrepareCrushing=crushing;_fmSignature=null;ReadFm();Notify();}
    private IReadOnlyDictionary<int,BreedingSpecies> _mountSpecies=new Dictionary<int,BreedingSpecies>();
    private int _mountNamesLoading;
    private int _petLoading;
    private long _focusIntent;
    private bool _replayingCrush;
    private CrushHistorySessionWriteModel? _currentCrush;
    public void BeginCrush() { _replayingCrush=false;_currentCrush=null; }
    public DofusClientWindowKey? TargetWindow => clients.SelectedClient?.Key;
    public event Action<string>? ModuleRequested;
    public void OpenModule(string slug) => ModuleRequested?.Invoke(slug);
    public OverlayModuleView Module(WorkspaceOverlayKind kind) => _modules.GetValueOrDefault(kind) ??
        new(OverlayWindowsService.Title(kind),[],kind switch{
            WorkspaceOverlayKind.Crush=>"Aucun concassage relevé.",WorkspaceOverlayKind.Forgemaging=>"Déposez un objet à l’atelier.",
            WorkspaceOverlayKind.Breeding=>"Ouvrez vos enclos dans Dofus.",WorkspaceOverlayKind.Pets=>"Synchronisez l’inventaire et consultez les prix en HDV.",
            _=>"Choisissez un objectif dans BestCrush."},kind switch{WorkspaceOverlayKind.Forgemaging=>"forgemagie",WorkspaceOverlayKind.Breeding=>"elevage",WorkspaceOverlayKind.Pets=>"familiers-xp",_=>"routine"});
    public void RefreshModule(WorkspaceOverlayKind kind)
    {
        if(kind==WorkspaceOverlayKind.Runes){_runesRequested=true;RuneInputsChanged();_=LoadRuneRankingAsync();}
        if(kind==WorkspaceOverlayKind.Pets){_petsRequested=true;_=Task.Run(LoadPetsAsync);}
        if(kind==WorkspaceOverlayKind.Breeding){ReadBreeding();_=LoadMountNamesAsync();}
        if(kind==WorkspaceOverlayKind.Forgemaging)ReadFm();
        if(kind==WorkspaceOverlayKind.Journey)ReadJourney();
    }
    private async Task PollAsync(CancellationToken token)
    {
        try
        {
            while(!token.IsCancellationRequested)
            {
                await Task.Delay(1000,token);
                try
                {
                    ReadFm(); ReadBreeding();
                    if(_runesRequested && _runesDirty)_=LoadRuneRankingAsync();
                    if(_ownedStockDirty)_=RefreshOwnedFocusAsync();
                    if(++_pollTicks%5==0)ReadJourney();
                    if(_petsRequested && _petDirty)_=Task.Run(LoadPetsAsync);
                    Notify();
                }
                catch(Exception ex) when(!token.IsCancellationRequested){logger.LogWarning(ex,"Overlay live refresh failed");}
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){}
        catch(Exception ex){logger.LogWarning(ex,"Overlay live readings interrupted");}
    }
    public async Task LoadOwnedAsync(bool inventoryOnly=false,long? itemId=null)
    {
        if(TargetWindow is not { } window || stock.ForWindow(window) is not {ServerName:{ } server} source)
        {Owned=[];OwnedServer=null;Notify();return;}
        using var scope=scopes.CreateScope();var s=scope.ServiceProvider;
        var result=await ForgemagedEquipmentStock.LoadAsync(source,server,s.GetRequiredService<ForgemagingSessionStore>(),
            s.GetRequiredService<ForgemagingDataService>(),s.GetRequiredService<ActivityCatalogService>(),
            s.GetRequiredService<CoefficientService>(),s.GetRequiredService<IBestCrushSettingsProvider>(),true,_stop.Token,itemId);
        if(TargetWindow==window && ReferenceEquals(source,stock.ForWindow(window)))
        {Owned=result.Where(item=>!inventoryOnly || item.Container=="inventory").Select(item=>new OverlayOwnedChoice(item)).ToArray();OwnedServer=server;Notify();}
    }
    internal async Task FocusAcquisitionAsync(ObservedEquipmentAcquisition acquisition,NetworkCaptureLease lease)
    {
        var intent=Interlocked.Increment(ref _focusIntent);
        if(lease.Window is not { } window || TargetWindow!=window || !servers.IsCaptureLeaseActive(lease))return;
        try
        {
            // The stock observer and acquisition correlation complete independently.
            for(int attempt=0;attempt<8;attempt++)
            {
                if(intent!=Volatile.Read(ref _focusIntent) || !servers.IsCaptureLeaseActive(lease) || TargetWindow!=window)return;
                if(stock.ForWindow(window) is { } source)
                {
                    using var scope=scopes.CreateScope();var s=scope.ServiceProvider;
                    var rows=await ForgemagedEquipmentStock.LoadAsync(source,lease.ServerName,s.GetRequiredService<ForgemagingSessionStore>(),
                        s.GetRequiredService<ForgemagingDataService>(),s.GetRequiredService<ActivityCatalogService>(),s.GetRequiredService<CoefficientService>(),
                        s.GetRequiredService<IBestCrushSettingsProvider>(),true,_stop.Token,acquisition.ItemId);
                    var key=ForgemagedEquipmentStock.InstanceKey(lease.ServerName,acquisition.ItemId,acquisition.Uid);
                    if(rows.FirstOrDefault(row=>row.Key==key) is { } owned && intent==Volatile.Read(ref _focusIntent) && servers.IsCaptureLeaseActive(lease) && TargetWindow==window)
                    {await FocusOwnedAsync(owned,lease.ServerName);return;}
                }
                await Task.Delay(150,_stop.Token);
            }
            using var fallbackScope=scopes.CreateScope();
            var item=await fallbackScope.ServiceProvider.GetRequiredService<ItemsService>().GetEquipmentAsync(acquisition.ItemId,_stop.Token);
            if(item is not null && intent==Volatile.Read(ref _focusIntent) && servers.IsCaptureLeaseActive(lease) && TargetWindow==window)
                await FocusCoreAsync(item,lease.ServerName,null,acquisition.Acquisition.Kind==EquipmentAcquisitionKind.Craft?OverlayAcquisition.Craft:OverlayAcquisition.Purchase,
                    acquisition.Acquisition.PricePaid,"Jet réel indisponible",0,true,
                    ForgemagedEquipmentStock.InstanceKey(lease.ServerName,acquisition.ItemId,acquisition.Uid));
        }
        catch(Exception ex) when(!_stop.IsCancellationRequested){logger.LogWarning(ex,"Could not focus acquired equipment");}
    }
    private void FmChanged(){ReadFm();_=FocusLiveFmAsync();Notify();}
    private async Task FocusLiveFmAsync()
    {
        var server=ActiveGameServer;var view=fm.View(server);
        if(!view.CanTrack || view.Session is not { } session || session.Lines.Any(l=>l.Current is null))return;
        var signature=$"{view.StockKey}:{session.LastObservedRollAtUtc:O}:{session.Attempts.Count}:"+
            string.Join(';',session.Lines.Select(l=>$"{l.Characteristic}:{l.Current}"));
        if(signature==_lastFm)return;
        _lastFm=signature;var intent=Interlocked.Increment(ref _focusIntent);var window=TargetWindow;
        try
        {
            using var scope=scopes.CreateScope();
            var item=await scope.ServiceProvider.GetRequiredService<ItemsService>().GetEquipmentAsync(session.ItemId,_stop.Token);
            if(item is null || TargetWindow!=window || intent!=Volatile.Read(ref _focusIntent) || fm.View(server).StockKey!=view.StockKey)return;
            var acquisition=server is not null && view.StockKey is { } stockKey
                ? scope.ServiceProvider.GetRequiredService<ForgemagingSessionStore>().GetAcquisitions(server,[(session.ItemId,stockKey)]).GetValueOrDefault(stockKey) : null;
            long? cost=acquisition?.PricePaid;
            if(acquisition?.Kind==EquipmentAcquisitionKind.Craft && server is not null)
                cost=(await scope.ServiceProvider.GetRequiredService<ForgemagingDataService>().GetEquipmentCraftCostsAsync(server,[session.ItemId],_stop.Token)).GetValueOrDefault(session.ItemId)?.TotalCost;
            if(TargetWindow!=window || intent!=Volatile.Read(ref _focusIntent))return;
            await FocusCoreAsync(item,server,session.Lines.ToDictionary(l=>l.Characteristic,l=>(double)l.Current!.Value),
                acquisition?.Kind==EquipmentAcquisitionKind.Craft?OverlayAcquisition.Craft:OverlayAcquisition.Purchase,
                cost,"Jet réel · FM",session.KnownSpent,session.UnpricedAttempts==0,view.StockKey);
            ShowRequested?.Invoke(WorkspaceOverlayKind.Forgemaging);
        }
        catch(Exception ex) when(!_stop.IsCancellationRequested){logger.LogWarning(ex,"Could not focus workshop equipment");}
    }
    private void ReadFm()
    {
        var view=fm.View(ActiveGameServer);
        if(!view.CanTrack || view.Session is not { } session){_modules.TryRemove(WorkspaceOverlayKind.Forgemaging,out _);return;}
        var signature=$"{_fmPrepareCrushing}:{view.StockKey}:{session.LastObservedRollAtUtc:O}:{session.Attempts.Count}:{session.Sink}:{session.Budget}:{view.Crushing?.CoefficientPercent}:"+
            string.Join(';',view.Runes.Select(r=>$"{r.Id}:{r.UnitPrice}"))+string.Join(';',session.Lines.Select(l=>$"{l.Characteristic}:{l.Current}:{l.Target}"));
        if(signature==_fmSignature)return;_fmSignature=signature;
        var estimate=ForgemagingCrushAssessment.EstimateRoll(session.Level,session.Lines,view.Runes,view.Crushing);
        var card=new OverlayActionCard(session.ItemName,$"Niv. {session.Level}",
            [new("Dépensé",Money(session.KnownSpent)),new("Reliquat",session.Sink?.ToString("N1")??"—"),new("Brisage estimé",Money(estimate.Best?.Value))],[])
        {Key=view.StockKey,ItemId=session.ItemId,IconId=view.EquipmentIconId,Forgemaging=new(session,view.Runes)};
        _modules[WorkspaceOverlayKind.Forgemaging]=new("Forgemagie",[card],"","forgemagie");
    }
    public Task AddFmExtraAsync(string? recording, Characteristic stat) => fm.AddExtraLineAsync(recording, stat);
    public Task RemoveFmExtraAsync(string? recording, Characteristic stat) => fm.RemoveGoalLineAsync(recording, stat);

    private async Task LoadMountNamesAsync()
    {
        if(_mountSpecies.Count>0 || Interlocked.Exchange(ref _mountNamesLoading,1)!=0)return;
        try
        {
            using var scope=scopes.CreateScope();
            _mountSpecies=(await scope.ServiceProvider.GetRequiredService<ActivityCatalogService>().GetBreedingSpeciesAsync(_stop.Token))
                .ToDictionary(m=>(int)m.Id);
            _breedingSignature=null;ReadBreeding();Notify();
        }
        catch(Exception ex) when(!_stop.IsCancellationRequested){logger.LogDebug(ex,"Mount names unavailable");}
        finally{_mountNamesLoading=0;}
    }
    private void ReadBreeding()
    {
        using var scope=scopes.CreateScope();var breeding=scope.ServiceProvider.GetRequiredService<LiveBreedingService>();
        if(TargetWindow is not { } window || breeding.ViewForWindow(window) is not { } view)
        {_modules.TryRemove(WorkspaceOverlayKind.Breeding,out _);return;}
        var goals=breeding.ResaleGoalsForWindow(window);var now=DateTime.UtcNow;
        var signature=string.Join(';',view.Enclosures.Select(e=>$"{e.Enclosure}:{e.ObservedAtUtc:O}:{OverlayBreedingPresentation.IsOld(e.ObservedAtUtc,now)}"))+
            $":{view.StableObservedAtUtc:O}:{view.Stable.Count}:"+string.Join(';',goals.OrderBy(g=>g.Key));
        if(signature==_breedingSignature)return;_breedingSignature=signature;
        var cards=view.Enclosures.Select(reading=>OverlayBreedingPresentation.Enclosure(reading,_mountSpecies,goals,now)).ToList();
        if(view.Stable.Count>0)cards.Add(new("Étable",$"{view.Stable.Count} montures",[],[])
        {Mounts=view.Stable.Select(entry=>OverlayBreedingPresentation.Mount(entry.Reading,_mountSpecies)).ToArray(),
            OldReading=view.StableObservedAtUtc is {} stableAt && OverlayBreedingPresentation.IsOld(stableAt,now)});
        _modules[WorkspaceOverlayKind.Breeding]=new("Élevage",cards,"Ouvrez vos enclos dans Dofus.","elevage");
    }
    internal async Task LoadPetsAsync()
    {
        if(Interlocked.Exchange(ref _petLoading,1)!=0)return;
        var window=TargetWindow;var server=ActiveGameServer;var revision=Interlocked.Read(ref _petRevision);
        bool Current()=>TargetWindow==window && ActiveGameServer==server && revision==Interlocked.Read(ref _petRevision);
        if(server is null || DateTime.UtcNow<_petRetryAfter || !_petDirty && _pets?.Server==server && DateTime.UtcNow-_petLoadedAt<TimeSpan.FromSeconds(15)){_petLoading=0;return;}
        try
        {
            _petDirty=false;PetError=null;Notify();
            using var scope=scopes.CreateScope();
            var report=await scope.ServiceProvider.GetRequiredService<PetAnalysisService>().LoadAsync(window,server,_stop.Token,partial=>{
                if(Current())PublishPets(partial);return Task.CompletedTask;
            });
            if(report is not null && Current()){PublishPets(report);_petLoadedAt=DateTime.UtcNow;}
        }
        catch(OperationCanceledException) when(!_stop.IsCancellationRequested){_petDirty=true;}
        catch(Exception ex) when(!_stop.IsCancellationRequested)
        {
            if(Current()){PetError="Analyse indisponible";_petDirty=true;_petRetryAfter=DateTime.UtcNow.AddSeconds(15);}
            logger.LogWarning(ex,"Could not load pet overlay");
        }
        finally{if(!Current())_petDirty=true;_petLoading=0;Notify();}
    }
    public void PublishPets(PetOpportunityReport report)
    {
        if(!string.Equals(report.Server,ActiveGameServer,StringComparison.OrdinalIgnoreCase))return;
        _pets=report;
        var cards=report.Purchases.Select(p=>{
            var plan=p.Best;var nutrition=plan?.Nutrition;
            var food=nutrition?.Meals.Select(m=>new OverlayPriceLine(m.Food.Name,m.Quantity,null,null,
                $"Stock {m.FromStock} · craft {m.Crafted} · achat {m.Bought}",false,m.Food.Id)).ToArray()??[];
            return new OverlayActionCard(p.Species.Name,$"Niv. {p.Purchase?.Level.ToString()??"—"} → {plan?.Level.ToString()??"—"}",
                [new("À débourser",Money(nutrition?.CashRequired)),new("Vente conseillée",Money(plan?.SalePrice)),new("Gain estimé",Money(plan?.Margin),plan?.Margin>=0?"positive":"negative")],
                food,plan is null?p.Issue:"Nourrir jusqu’au niveau conseillé, puis vendre.")
                {Key=$"{p.Species.Id}:{p.Purchase?.Level}",ItemId=p.Species.Id,IconId=p.Species.IconId};
        }).ToArray();
        _modules[WorkspaceOverlayKind.Pets]=new("Familiers",cards,report.AnalysisComplete?"Achat rentable non chiffré":"Analyse en cours…","familiers-xp");Notify();
    }
    private void ReadJourney()
    {
        using var scope=scopes.CreateScope();var s=scope.ServiceProvider;var cards=new List<OverlayActionCard>();
        try
        {
            if(ProductionTrackingScope.For(clients,TargetWindow,servers.ServerName ?? "") is { } key)
            {
                var breeding=s.GetRequiredService<BreedingObjectiveProgressStore>().Read(key);
                var selected=breeding.Selected is { } id?breeding.Goals.GetValueOrDefault(id):null;
                if(selected is not null)
                {
                    var step=selected.Steps.Select((value,index)=>(value,index)).FirstOrDefault(v=>!selected.Done.Contains(v.index));
                    cards.Add(new("Objectif élevage",null,[],[],step.value?.Name,BreedingObjectiveProgressStore.Percent(selected)));
                }
                var pets=s.GetRequiredService<PetObjectiveProgressStore>().Read(key);
                foreach(var goal in pets.Goals.Values.Where(g=>g.Percent<100).Take(2))
                    cards.Add(new(goal.Name,$"Niveau cible {goal.TargetLevel}",[],[],goal.PendingListings.Count>0?"Attendre la vente encaissée":"Poursuivre le nourrissage",goal.Percent));
            }
            var routine=s.GetRequiredService<WeeklyRoutineStore>().Read();var today=WeeklyRoutinePlanner.Today(DateTimeOffset.UtcNow);
            foreach(var task in routine.Activities.Where(a=>WeeklyRoutinePlanner.Eligible(routine,a) && !WeeklyRoutinePlanner.DoneOn(routine,a,today)).OrderBy(a=>a.Priority).Take(3))
                cards.Add(new(task.Title,null,task.EstimatedNetKamas is { } gain?[new("Gain estimé",Money(gain))]:[],[],task.Notes));
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){logger.LogDebug(ex,"Overlay journal unavailable");}
        _modules[WorkspaceOverlayKind.Journey]=new("Parcours",cards,"Choisissez un objectif dans BestCrush.","routine");
    }
    public void PublishCrush(CrushSessionSnapshot snapshot,string title,string server)
    {
        if(snapshot.NetworkCrushCount==0 || _replayingCrush)return;
        if(_currentCrush is { } current && current.ServerName==server)
        {
            var repriced=current with {TotalValue=snapshot.TotalValue,DiscountedTotalValue=snapshot.DiscountedTotalValue,
                Runes=current.Runes.Select(r=>snapshot.Runes.FirstOrDefault(n=>n.Name==r.RuneName) is { } live?r with {Value=live.Value}:r).ToArray()};
            PublishCrushHistory(repriced,show:false);return;
        }
        var lines=snapshot.Runes.Select(r=>new OverlayPriceLine(r.Name,r.Quantity,r.Value,r.Quantity>0?r.Value/r.Quantity:null)).ToArray();
        _modules[WorkspaceOverlayKind.Crush]=new("Résultat du concassage",[new(title,$"{snapshot.NetworkCrushCount} objet(s) · {server}",
            [],lines){Crush=new(null,null,null,snapshot.DiscountedTotalValue)}],"","concassage");Notify();
    }
    public void PublishCrushHistory(CrushHistorySessionWriteModel session,bool show=true)
    {
        if(_replayingCrush && !show)return;
        if(!show)_currentCrush=session;
        var results=session.Equipments.Select(e=>e.Comparison).ToArray();
        double? purchase=results.Length>0 && results.All(c=>(c?.RecordedAcquisitionCost??c?.EstimatedCraftCost) is not null)
            ? results.Sum(c=>(double)(c!.RecordedAcquisitionCost??c.EstimatedCraftCost)!.Value):null;
        double? fm=results.Length>0 && results.All(c=>c?.UnpricedFmRunes==0)
            ? results.Sum(c=>c!.KnownFmCost):null;
        bool estimatedPrice=results.Any(c=>c?.RecordedAcquisitionCost is null && c?.EstimatedCraftCost is not null);
        double? estimate=results.Length>0 && results.All(c=>c?.EstimatedValue is not null)?results.Sum(c=>c!.EstimatedValue):null;
        var lines=session.Runes.Select(r=>new OverlayPriceLine(r.RuneName,r.Quantity,r.Value,r.Quantity>0?r.Value/r.Quantity:null)).ToArray();
        _modules[WorkspaceOverlayKind.Crush]=new("Résultat du concassage",[new(string.Join(" · ",session.Equipments.Select(e=>e.EquipmentName)),
            session.CompletedAtUtc.ToLocalTime().ToString("dd/MM HH:mm")+" · "+session.ServerName,[],lines)
            {Crush=new(purchase,fm,estimate,session.DiscountedTotalValue,estimatedPrice)}],"","concassage");Notify();
        if(show)ShowRequested?.Invoke(WorkspaceOverlayKind.Crush);
    }
    public void RecallCrush(CrushHistorySession session)
    {
        _replayingCrush=true;
        PublishCrushHistory(new(session.ServerName,session.StartedAtUtc,session.CompletedAtUtc,
        session.TotalValue,session.DiscountPercent,session.DiscountedTotalValue,
        session.Equipments.Select(e=>new CrushHistoryEquipmentWriteModel(e.DofusDbId,e.EquipmentName,e.CoefficientPercent,e.CoefficientSource,e.RowY){Comparison=e.ReadComparison()}).ToArray(),
        session.Runes.Select(r=>new CrushHistoryRuneWriteModel(r.DofusDbId,r.RuneName,r.Quantity,r.Value,[])).ToArray()));
    }
    private static string Money(double? value)=>value is { } v?$"{v:N0} K":"—";
    private static string Countdown(TimeSpan value)=>value<=TimeSpan.Zero?"Maintenant":value.TotalHours>=1?$"{(int)value.TotalHours} h {value.Minutes:00}":$"{value.Minutes} min {value.Seconds:00} s";
}
