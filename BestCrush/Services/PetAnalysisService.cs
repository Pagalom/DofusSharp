using BestCrush.Network.Protocol;
using Microsoft.Extensions.DependencyInjection;

namespace BestCrush.Services;

// One current analysis, including progressive results, shared by all UI surfaces.
// No character state or preferences are written to disk here.
public sealed class PetAnalysisService : IDisposable
{
    private readonly object _gate=new();
    private readonly Func<DofusClientWindowKey?,string?,CancellationToken,Func<PetOpportunityReport,Task>,Task<PetOpportunityReport?>> _load;
    private readonly CurrentServerState? _servers;
    private CurrentDofusClientsState? _clients;
    private (DofusClientWindowKey? Window,string? Name,string? Build) _clientContext;
    private Action? _unsubscribe;
    private Entry? _entry;
    private bool _disposed;
    public event Action? Changed;
    private sealed class Entry(DofusClientWindowKey? window,string? server)
    {
        public DofusClientWindowKey? Window {get;}=window;
        public string? Server {get;}=server;
        public CancellationTokenSource Stop {get;}=new();
        public Task<PetOpportunityReport?> Work {get;set;}=null!;
        public PetOpportunityReport? Latest;
        public readonly Dictionary<Guid,Func<PetOpportunityReport,Task>> Listeners=[];
        public int Waiters;
    }
    internal PetAnalysisService(Func<DofusClientWindowKey?,string?,CancellationToken,Func<PetOpportunityReport,Task>,Task<PetOpportunityReport?>> load,
        CurrentDofusClientsState? clients=null)
    {
        _load=load;_clients=clients;_clientContext=ClientContext();
        if(clients is not null)clients.Changed+=ClientChanged;
    }
    public PetAnalysisService(IServiceScopeFactory scopes,CurrentServerState servers,CurrentDofusClientsState clients,
        LiveStockService stock,CurrentWindowProfessionsState professions,BestCrushSettingsService settings,
        GlobalSalesRepository sales,LivePetFeedingService feeding,MarketDataChangeNotifier prices,PetFoodPreferences foods)
        :this(async(window,server,token,publish)=>
        {
            using var scope=scopes.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<PetOpportunityService>().LoadAsync(window,server,token,publish);
        },clients)
    {
        _servers=servers;
        stock.Changed+=Invalidate;professions.Changed+=Invalidate;settings.BreedingSettingsChanged+=Invalidate;
        servers.PlayedServerChanged+=Invalidate;servers.ServerSelectionChanged+=ServerSelectionChanged;
        sales.PetPricesChanged+=ServerChanged;sales.ListingsChanged+=ServerChanged;sales.Changed+=ServerChanged;
        feeding.Changed+=ServerChanged;prices.Changed+=PriceChanged;foods.Changed+=Invalidate;
        _unsubscribe=()=>
        {
            stock.Changed-=Invalidate;professions.Changed-=Invalidate;settings.BreedingSettingsChanged-=Invalidate;
            servers.PlayedServerChanged-=Invalidate;servers.ServerSelectionChanged-=ServerSelectionChanged;
            sales.PetPricesChanged-=ServerChanged;sales.ListingsChanged-=ServerChanged;sales.Changed-=ServerChanged;
            feeding.Changed-=ServerChanged;prices.Changed-=PriceChanged;foods.Changed-=Invalidate;
        };
    }
    public async Task<PetOpportunityReport?> LoadAsync(DofusClientWindowKey? window,string? server,CancellationToken token,
        Func<PetOpportunityReport,Task>? publish=null)
    {
        token.ThrowIfCancellationRequested();
        server=window is { } key ? _servers?.GetWindowCaptureLease(key)?.ServerName ?? server : server;
        Entry entry;Entry? previous=null;PetOpportunityReport? latest;Guid listener=Guid.NewGuid();
        lock(_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed,this);
            if(_entry is null || _entry.Window!=window || !string.Equals(_entry.Server,server,StringComparison.OrdinalIgnoreCase))
            {
                previous=_entry;_entry=new(window,server);
                var created=_entry;
                created.Work=Task.Run(()=>RunAsync(created));
            }
            entry=_entry;entry.Waiters++;latest=entry.Latest;
            if(publish is not null)entry.Listeners[listener]=publish;
        }
        previous?.Stop.Cancel();
        try
        {
            if(latest is not null && publish is not null)await publish(latest);
            return await entry.Work.WaitAsync(token);
        }
        finally
        {
            bool cancel;
            lock(_gate)
            {
                entry.Listeners.Remove(listener);entry.Waiters--;
                cancel=entry.Waiters==0 && !entry.Work.IsCompleted;
                if(cancel && ReferenceEquals(_entry,entry))_entry=null;
            }
            if(cancel)entry.Stop.Cancel();
        }
    }
    private async Task<PetOpportunityReport?> RunAsync(Entry entry)
    {
        try
        {
            var report=await _load(entry.Window,entry.Server,entry.Stop.Token,partial=>PublishAsync(entry,partial));
            entry.Stop.Token.ThrowIfCancellationRequested();
            if(report is not null)await PublishAsync(entry,report);
            return report;
        }
        catch
        {
            lock(_gate){if(ReferenceEquals(_entry,entry))_entry=null;}
            throw;
        }
    }
    private async Task PublishAsync(Entry entry,PetOpportunityReport report)
    {
        Func<PetOpportunityReport,Task>[] listeners;
        lock(_gate)
        {
            if(!ReferenceEquals(_entry,entry) || entry.Stop.IsCancellationRequested)return;
            entry.Latest=report;listeners=entry.Listeners.Values.ToArray();
        }
        foreach(var listener in listeners)
        {
            try{await listener(report);}
            catch(OperationCanceledException){} // A closed view cannot cancel another view's analysis.
            catch(ObjectDisposedException){}
        }
    }
    internal void Invalidate()
    {
        Entry? old;
        lock(_gate){old=_entry;_entry=null;}
        old?.Stop.Cancel();Changed?.Invoke();
    }
    private void ServerChanged(string server)
    {
        lock(_gate){if(server.Length>0 && _entry is not null && !string.Equals(_entry.Server,server,StringComparison.OrdinalIgnoreCase))return;}
        Invalidate();
    }
    private (DofusClientWindowKey?,string?,string?) ClientContext()
    {var client=_clients?.SelectedClient;return(client?.Key,client?.DisplayName,client?.ClientBuild);}
    private void ClientChanged()
    {
        var context=ClientContext();
        lock(_gate){if(context==_clientContext)return;_clientContext=context;}
        Invalidate(); // Foreground changes alone must not restart a completed analysis.
    }
    private void ServerSelectionChanged()
    {
        lock(_gate)
        {
            if(_entry?.Window is { } window && _servers?.GetWindowCaptureLease(window) is not null)return;
        }
        Invalidate();
    }
    private void PriceChanged(object? sender,MarketDataChangedEventArgs change)
    {
        lock(_gate)
        {
            if(_entry is not { } entry || !string.Equals(entry.Server,change.ServerName,StringComparison.OrdinalIgnoreCase))return;
            if(change.DofusDbId!=0 && entry.Latest is {RelevantPriceItems.Count:>0} report && !report.RelevantPriceItems.Contains(change.DofusDbId))return;
        }
        Invalidate();
    }
    public void Dispose()
    {
        Entry? old;
        lock(_gate){if(_disposed)return;_disposed=true;old=_entry;_entry=null;}
        _unsubscribe?.Invoke();if(_clients is not null)_clients.Changed-=ClientChanged;old?.Stop.Cancel();
    }
}
