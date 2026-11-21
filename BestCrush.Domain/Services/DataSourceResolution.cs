using BestCrush.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace BestCrush.Domain.Services;

public static class DataSourceResolution
{
    public static async Task<IReadOnlyDictionary<(long Item,int Quantity),MarketPriceSource>> ReadAsync(
        BestCrushDbContext db,IDataPriorityProvider priority,string server,MarketObjectType type,
        DataValueKind kind=DataValueKind.Price,CancellationToken token=default)
    {
        if(priority.Priority!=DataPriority.Custom)return new Dictionary<(long,int),MarketPriceSource>();
        return await db.DataSourcePreferences.AsNoTracking().Where(p=>p.ServerName==server && p.ObjectType==type && p.Kind==kind)
            .ToDictionaryAsync(p=>new ValueTuple<long,int>(p.ItemId,p.Quantity),p=>p.Source,token);
    }
    public static MarketPriceSource Resolve(IDataPriorityProvider priority,
        IReadOnlyDictionary<(long Item,int Quantity),MarketPriceSource> choices,long item,int quantity)
    {
        if(priority.Priority==DataPriority.Custom)
        {
            if(choices.TryGetValue((item,quantity),out var cell))return cell;
            if(choices.TryGetValue((item,0),out var row))return row;
            if(choices.TryGetValue((0,0),out var tab))return tab;
        }
        var fallback=priority.Priority==DataPriority.Custom ? priority.CustomDefault : priority.Priority;
        return fallback==DataPriority.InGameAutomatic ? MarketPriceSource.InGameAutomatic : MarketPriceSource.Manual;
    }
}
