using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BestCrush.Services;

public sealed record RuneRankingEntry(Rune Rune,double? Weight,double? Ratio,(int Lot,long Price)? Reference)
{
    public string Tooltip=>Reference is {} quote
        ? $"{quote.Price/(double)quote.Lot:N2} K/u · Densité {Weight:0.##}"
        : "Prix à relever à l’HDV";
}

public sealed partial class OverlayWorkspaceService
{
    public IReadOnlyList<RuneRankingEntry> RuneRanking {get;private set;}=[];
    public string? RuneRankingServer {get;private set;}
    public string? RuneRankingError {get;private set;}
    private volatile bool _runesRequested,_runesDirty=true;
    private int _runesLoading;
    public bool RuneRankingLoading=>Volatile.Read(ref _runesLoading)!=0;
    private void RuneInputsChanged(){_runesDirty=true;}
    private async Task LoadRuneRankingAsync()
    {
        if(Interlocked.Exchange(ref _runesLoading,1)!=0)return;
        _runesDirty=false;
        var server=ActiveGameServer;var window=TargetWindow;
        try
        {
            RuneRankingError=null;
            if(server is null){RuneRanking=[];RuneRankingServer=null;return;}
            using var scope=scopes.CreateScope();var services=scope.ServiceProvider;
            var runes=await services.GetRequiredService<RunesService>().GetLocalRunesAsync(_stop.Token);
            var quotes=await services.GetRequiredService<MarketPriceService>().GetLatestObservationsForServerAsync(MarketObjectType.Rune,server,_stop.Token);
            var rows=RankRunes(runes,quotes);
            if(server!=ActiveGameServer || window!=TargetWindow)return;
            RuneRanking=rows;RuneRankingServer=server;
        }
        catch(OperationCanceledException) when(_stop.IsCancellationRequested){}
        catch(Exception ex)
        {RuneRankingError="Classement indisponible";logger.LogWarning(ex,"Rune ranking unavailable.");}
        finally{Interlocked.Exchange(ref _runesLoading,0);Notify();}
    }
    internal static IReadOnlyList<RuneRankingEntry> RankRunes(IEnumerable<Rune> runes,
        IReadOnlyDictionary<(long DofusDbId,int Quantity),MarketPriceObservation> quotes)=>
        runes.Select(rune=>
        {
            long? Price(int lot)=>quotes.TryGetValue((rune.DofusDbId,lot),out var quote)?quote.Price:null;
            var weight=RuneMarketRanking.Weight(rune);
            return new RuneRankingEntry(rune,weight,RuneMarketRanking.Ratio(weight,Price(1),Price(10),Price(100),Price(1000)),
                RuneMarketRanking.Reference(Price(1),Price(10),Price(100),Price(1000)));
        }).OrderByDescending(row=>row.Ratio).ThenBy(row=>row.Rune.Name,StringComparer.CurrentCultureIgnoreCase).ThenBy(row=>row.Rune.DofusDbId).ToArray();
}
