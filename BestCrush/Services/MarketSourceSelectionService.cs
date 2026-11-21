using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace BestCrush.Services;

// Source choices contain market coordinates only, never character data.
public sealed class MarketSourceSelectionService(IDbContextFactory<BestCrushDbContext> database,
    IDataPriorityProvider priority,MarketDataChangeNotifier changes)
{
    private readonly SemaphoreSlim _gate=new(1,1);
    public event Action? Changed;
    public async Task ChooseAsync(string server,MarketObjectType type,long item,int quantity,MarketPriceSource source,
        DataValueKind kind=DataValueKind.Price,CancellationToken token=default)
    {
        if(string.IsNullOrWhiteSpace(server) || item<0 || quantity<0 || item==0 && quantity!=0 ||
            source is not (MarketPriceSource.Manual or MarketPriceSource.InGameAutomatic))throw new ArgumentException("Invalid source choice.");
        await _gate.WaitAsync(token);
        try
        {
            await using var db=await database.CreateDbContextAsync(token);
            await using var transaction=await db.Database.BeginTransactionAsync(token);
            // A whole-row/tab action replaces its more specific choices.
            await db.DataSourcePreferences.Where(p=>p.ServerName==server && p.ObjectType==type && p.Kind==kind &&
                (item==0 || p.ItemId==item) && (quantity==0 || p.Quantity==quantity))
                .ExecuteDeleteAsync(token);
            db.DataSourcePreferences.Add(new(){ServerName=server,ObjectType=type,Kind=kind,ItemId=item,Quantity=quantity,Source=source});
            await db.SaveChangesAsync(token);await transaction.CommitAsync(token);
            priority.Priority=DataPriority.Custom;
        }
        finally{_gate.Release();}
        changes.Notify(type,item,server,quantity);Changed?.Invoke();
    }
    public async Task SetGlobalAsync(DataPriority value,CancellationToken token=default)
    {
        if(!Enum.IsDefined(value))throw new ArgumentOutOfRangeException(nameof(value));
        (MarketObjectType Type,string Server)[] scopes;
        await _gate.WaitAsync(token);
        try
        {
            await using var db=await database.CreateDbContextAsync(token);
            var market=await db.MarketPriceObservations.Select(p=>new{Type=p.ObjectType,Server=p.ServerName}).Distinct().ToArrayAsync(token);
            var coefficients=await db.CoefficientObservations.Select(p=>p.ServerName).Distinct().ToArrayAsync(token);
            scopes=market.Select(p=>(p.Type,p.Server)).Concat(coefficients.Select(s=>(MarketObjectType.Equipment,s))).Distinct().ToArray();
            if(value!=DataPriority.Custom)await db.DataSourcePreferences.ExecuteDeleteAsync(token);
            priority.Priority=value;
        }
        finally{_gate.Release();}
        foreach(var scope in scopes)changes.Notify(scope.Type,0,scope.Server);
        Changed?.Invoke();
    }
}
