using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BestCrush.Services;

/// <summary>Shared view state for native overlays. Network identity remains with the verified capture services.</summary>
public sealed partial class OverlayWorkspaceService(IServiceScopeFactory scopes, CurrentServerState servers,
    CurrentDofusClientsState clients, LiveStockService stock, LiveForgemagingService fm,
    MarketDataChangeNotifier prices, ILogger<OverlayWorkspaceService> logger,PetFoodPreferences? petFoods=null,
    PetAnalysisService? petAnalysis=null) : IDisposable
{
    public PetFoodPreferences PetFoods {get;}=petFoods ?? new();
    public event Action? Changed;
    public event Action<WorkspaceOverlayKind>? ShowRequested;
    public ProfitabilityOverlaySnapshot? Profitability { get; private set; }
    public OverlayProfitabilitySelection Selection { get; } = new();
    internal long FocusVersion => Interlocked.Read(ref _focusIntent);
    public string? Error { get; private set; }
    public bool Loading { get; private set; }
    public IReadOnlyList<OverlayOwnedChoice> Owned { get; private set; } = [];
    public string? OwnedServer {get;private set;}
    private string? ActiveGameServer => TargetWindow is { } window && servers.GetWindowCaptureLease(window) is { } lease
        ?lease.ServerName:servers.AutomaticRoutingEnabled?null:servers.ServerName;
    private long _revision;
    private readonly SemaphoreSlim _calculationGate=new(1,1);
    private Guid _focusId, _selectionFocusId;
    private IItem? _item;
    private string? _server;
    private string? _instanceIdentity;
    private bool _wasCrushed;
    private DofusClientWindowKey? _window, _contextWindow;
    private string? _contextServer;
    private IReadOnlyDictionary<Characteristic,double>? _roll;
    private OverlayAcquisition? _origin;
    private long? _paid;
    private double _fmCost;
    private bool _fmKnown = true;
    private string _rollLabel = "Jet moyen";
    private string? _lastFm;
    private readonly CancellationTokenSource _stop = new();
    private Task? _poll;
    public void Initialize()
    {
        if (_poll is not null) return;
        prices.Changed += PriceChanged; fm.Changed += FmChanged; clients.Changed += ClientChanged;
        stock.Changed += StockInputsChanged;
        servers.ServerSelectionChanged += ClientChanged;servers.PlayedServerChanged += ClientChanged;
        _contextWindow=clients.SelectedClient?.Key;_contextServer=ActiveGameServer;
        _=CrushingSearch;ReconcileEquipmentSearchServer();
        if(petAnalysis is not null)petAnalysis.Changed+=PetInputsChanged;
        _poll = Task.Run(()=>PollAsync(_stop.Token));
    }
    public Task FocusAsync(IItem item, string? server = null) => FocusCoreAsync(item,
        server ?? ActiveGameServer ?? servers.ServerName, null, null, null, "Jet moyen", 0, true);
    public async Task FocusOwnedAsync(OwnedForgemagingEquipment owned,string? server=null)
    {
        var intent=Interlocked.Increment(ref _focusIntent);var window=TargetWindow;
        using var scope=scopes.CreateScope();
        var equipment=await scope.ServiceProvider.GetRequiredService<ItemsService>().GetEquipmentAsync(owned.Equipment.DofusDbId,_stop.Token)
            ?? owned.Equipment;
        if(intent!=FocusVersion || TargetWindow!=window)return;
        await FocusCoreAsync(equipment,server??ActiveGameServer??servers.ServerName,
            owned.Roll?.Where(l=>l.Current.HasValue).ToDictionary(l=>l.Characteristic,l=>(double)l.Current!.Value),
            owned.Acquisition?.Kind==EquipmentAcquisitionKind.Craft?OverlayAcquisition.Craft:OverlayAcquisition.Purchase,
            owned.AcquisitionCost,owned.Roll is null?"Jet réel indisponible":"Jet réel",owned.KnownRuneCost,
            owned.UnpricedRunes==0 && !owned.UnobservedChanges &&
                (owned.Acquisition?.Kind==EquipmentAcquisitionKind.Purchase || !owned.FmCostUnknown),owned.Key);
    }
    private async Task FocusCoreAsync(IItem item, string? server, IReadOnlyDictionary<Characteristic,double>? roll,
        OverlayAcquisition? origin, long? paid, string label, double fmCost, bool fmKnown,string? instanceIdentity=null)
    {
        if (string.IsNullOrWhiteSpace(server)) return;
        Interlocked.Increment(ref _focusIntent);
        bool sameInstance=instanceIdentity is not null && _instanceIdentity==instanceIdentity && _server==server && _item?.DofusDbId==item.DofusDbId;
        if(!sameInstance){_focusId=Guid.NewGuid();_wasCrushed=false;}
        _instanceIdentity=instanceIdentity;
        _item=item;_server=server;_window=clients.SelectedClient?.Key;_roll=roll;_origin=origin;_paid=paid;
        _rollLabel=label;_fmCost=fmCost;_fmKnown=fmKnown;
        if(!sameInstance)Profitability=new(item,server,label,null,null,null,null,[],[],[],fmCost,fmKnown){FocusId=_focusId};
        Notify();
        await RefreshAsync(!sameInstance);
        ShowRequested?.Invoke(WorkspaceOverlayKind.Profitability);
    }
    public async Task RefreshAsync(bool reset = false)
    {
        var revision=Interlocked.Increment(ref _revision);
        var item=_item;var server=_server;var roll=_roll;var origin=_origin;var paid=_paid;
        var rollLabel=_rollLabel;var fmCost=_fmCost;var fmKnown=_fmKnown;var focusId=_focusId;
        if (item is null || server is null) return;
        Loading=true;Error=null;Notify();
        try { await _calculationGate.WaitAsync(_stop.Token); }
        catch(OperationCanceledException) when(_stop.IsCancellationRequested){return;}
        try
        {
            if(revision!=Volatile.Read(ref _revision))return;
            using var scope=scopes.CreateScope();
            var pricing=scope.ServiceProvider.GetRequiredService<MarketPriceService>();
            var calculator=scope.ServiceProvider.GetRequiredService<EquipmentProfitabilityService>();
            var context=await calculator.LoadContextAsync(server,_stop.Token,gameRunePricesOnly:true);
            EquipmentProfitabilityResult? crushing=null;
            IReadOnlyList<OverlayPriceLine> ingredients=[];
            double? craft=null;
            if (item is Equipment equipment)
            {
                // Missing real stats must not silently become a pessimistic or average roll.
                var lines=roll ?? equipment.Characteristics.ToDictionary(l=>l.Characteristic,l=>(l.From+l.To)/2.0);
                crushing=await calculator.CalculateAsync(equipment,server,context,includeThresholds:false,
                    includeUnpricedScenarios:true,allowRemoteCoefficient:false,observedLines:lines);
                crushing=AddDisplayScenarios(crushing,lines,context,pricing,scope.ServiceProvider);
                if (rollLabel == "Jet réel indisponible") crushing=crushing with { Scenarios=[] };
                craft=equipment.Recipe.Count+equipment.EquipmentRecipe.Count>0?crushing.CraftCost.TotalCost:null;
                ingredients=crushing.CraftCost.Resources.Select(r=>new OverlayPriceLine(r.ResourceName,r.RequiredQuantity,
                    r.Cost,r.PurchasedQuantity is > 0 ? r.Cost/(double)r.PurchasedQuantity : null,
                    r.SurplusQuantity is > 0 ? $"{r.PurchasedQuantity} achetés · {r.SurplusQuantity} conservés" : null,
                    !r.HasPrice || r.Purchase?.Freshness != DataFreshness.Fresh,r.DofusDbId)).ToArray();
            }
            else
            {
                try { (craft,ingredients)=await ProductRecipeAsync(item,context,pricing,scope.ServiceProvider); }
                catch(Exception ex) when(!_stop.IsCancellationRequested){logger.LogDebug(ex,"Product recipe unavailable for overlay");}
            }
            var type=item is CraftedProduct product ? product.MarketType : item is Equipment ? MarketObjectType.Equipment : item is Rune ? MarketObjectType.Rune : MarketObjectType.Resource;
            var quotes=StockCraftPlanner.PricesFor(context,type);
            var purchase=pricing.CalculateMinimumPurchaseCost(item.DofusDbId,1,quotes);
            var sale=pricing.CalculateValue(item.DofusDbId,1,quotes);
            double? buy=purchase?.TotalCost;
            if (origin == OverlayAcquisition.Purchase && (paid is not null || rollLabel.StartsWith("Jet réel",StringComparison.Ordinal))) buy=paid;
            if (origin == OverlayAcquisition.Craft && paid is not null) craft=paid;
            var checks=new[]{new OverlayPriceLine(item.Name,1,sale?.Value,sale?.Value,null,
                sale is null || sale.Value.Freshness != DataFreshness.Fresh,item.DofusDbId)};
            var runeChecks=crushing?.PotentialRunes.Select(r=>{
                var value=crushing.PotentialRuneUnitValues.GetValueOrDefault(r);
                bool known=crushing.PotentialRuneUnitValues.ContainsKey(r);
                return new OverlayPriceLine(r.Name,1,known?value.Value:null,known?value.Value:null,null,
                    !known || value.Freshness!=DataFreshness.Fresh,r.DofusDbId);
            }).ToArray() ?? [];
            if(item is Equipment {Type:EquipmentType.Pet or EquipmentType.Petsmount or EquipmentType.Mount})crushing=null;
            var purchaseDetails=quotes.Where(q=>q.Key.DofusDbId==item.DofusDbId && q.Value.Price>0)
                .OrderBy(q=>q.Key.Quantity).Select(q=>new OverlayPriceLine(item.Name,q.Key.Quantity,q.Value.Price,
                    q.Value.Price/(double)q.Key.Quantity,$"HDV · lot × {q.Key.Quantity}",
                    DataFreshnessEvaluator.Evaluate(q.Value.ObservedAtUtc)!=DataFreshness.Fresh,item.DofusDbId)).ToList();
            if(origin==OverlayAcquisition.Purchase)
                purchaseDetails.Insert(0,new(item.Name,1,buy,buy,"Prix d’obtention",buy is null,item.DofusDbId));
            if(purchaseDetails.Count==0)purchaseDetails.Add(new(item.Name,1,buy,buy,null,buy is null,item.DofusDbId));
            var value=new ProfitabilityOverlaySnapshot(item,server,rollLabel,crushing,buy,craft,sale?.Value,
                ingredients,checks,runeChecks,fmCost,fmKnown){FocusId=focusId,PurchaseDetails=purchaseDetails,WasCrushed=_wasCrushed};
            if (revision != Volatile.Read(ref _revision)) return;
            Profitability=value;
            if(reset || _selectionFocusId!=value.FocusId) Selection.Reset(value,origin);else Selection.Refresh(value);
            _selectionFocusId=value.FocusId;
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        { if (revision==Volatile.Read(ref _revision)) Error="Calcul indisponible"; logger.LogWarning(ex,"Overlay profitability failed"); }
        finally { _calculationGate.Release();if(revision==Volatile.Read(ref _revision)) {Loading=false;Notify();} }
    }
    public void ChooseRoute(OverlayRoute route) { Selection.SelectRoute(route);Notify(); }
    public void ChooseFocus(Characteristic? focus) { Selection.SelectFocus(focus);Notify(); }
    public void Notify() => Changed?.Invoke();
    public void Request(WorkspaceOverlayKind kind) => ShowRequested?.Invoke(kind);
    private void PriceChanged(object? sender,MarketDataChangedEventArgs args)
    {
        if(args.ObjectType==MarketObjectType.Rune && string.Equals(args.ServerName,ActiveGameServer,StringComparison.OrdinalIgnoreCase))RuneInputsChanged();
        var pets=_pets;
        if(pets is not null && string.Equals(args.ServerName,pets.Server,StringComparison.OrdinalIgnoreCase) &&
            (args.DofusDbId==0 || pets.RelevantPriceItems.Count==0 || pets.RelevantPriceItems.Contains(args.DofusDbId))) PetInputsChanged();
        var focused=Profitability;
        if(string.Equals(args.ServerName,_server,StringComparison.OrdinalIgnoreCase) &&
            (args.DofusDbId==0 || Loading || focused is null || focused.Item.DofusDbId==args.DofusDbId ||
                focused.Ingredients.Concat(focused.RuneChecks).Any(line=>line.ItemId==args.DofusDbId))) _=RefreshAsync();
    }
    private void ClientChanged()
    {
        var window=clients.SelectedClient?.Key;var server=ActiveGameServer;
        if(_contextWindow != window || _contextServer != server)
        {
            _contextWindow=window;_contextServer=server; Interlocked.Increment(ref _revision);Interlocked.Increment(ref _focusIntent);
            ReconcileEquipmentSearchServer();
            RuneRanking=[];RuneRankingServer=null;RuneInputsChanged();
            Profitability=null;_item=null;Owned=[];OwnedServer=null;_lastFm=null;_pets=null;_modules.Clear();
            _breedingSignature=null;_fmSignature=null;PetError=null;PetInputsChanged();Notify(); }
    }
    public void Dispose()
    { _stop.Cancel(); prices.Changed-=PriceChanged;fm.Changed-=FmChanged;clients.Changed-=ClientChanged;stock.Changed-=StockInputsChanged;
        _crushingSearch.Changed-=SharedCrushingSearchChanged;
        servers.ServerSelectionChanged-=ClientChanged;servers.PlayedServerChanged-=ClientChanged;
        if(petAnalysis is not null)petAnalysis.Changed-=PetInputsChanged; }
}
