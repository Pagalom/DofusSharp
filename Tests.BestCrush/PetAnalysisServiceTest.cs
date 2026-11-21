using System.Numerics;
using BestCrush.Network.Protocol;
using BestCrush.Services;

namespace Tests.BestCrush;

public sealed class PetAnalysisServiceTest
{
    private static PetOpportunityReport Report()=>new("A",[],[],[],new Dictionary<long,BigInteger>(),new Dictionary<long,BigInteger>(),true,true,true,true,0,0);

    [Fact]
    public async Task PageAndOverlayShareOneCalculationProgressAndCompletedResult()
    {
        int loads=0;var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete=Report();var partial=complete with {AnalysisComplete=false};
        using var analysis=new PetAnalysisService(async(window,server,token,publish)=>
        {
            Interlocked.Increment(ref loads);await publish(partial);started.SetResult();
            await release.Task.WaitAsync(token);return complete;
        });
        var pageReports=new List<PetOpportunityReport>();var overlayReports=new List<PetOpportunityReport>();
        var page=analysis.LoadAsync(null,"A",default,r=>{pageReports.Add(r);return Task.CompletedTask;});
        await started.Task;
        var overlay=analysis.LoadAsync(null,"a",default,r=>{overlayReports.Add(r);return Task.CompletedTask;});
        Assert.Same(partial,Assert.Single(overlayReports));
        release.SetResult();
        Assert.Same(complete,await page);Assert.Same(complete,await overlay);
        Assert.Same(complete,await analysis.LoadAsync(null,"A",default));
        Assert.Equal(1,loads);Assert.Contains(complete,pageReports);Assert.Contains(complete,overlayReports);
    }

    [Fact]
    public async Task ClosingOneViewDoesNotCancelTheOtherViewsCalculation()
    {
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken shared=default;
        using var analysis=new PetAnalysisService(async(window,server,token,publish)=>
        {shared=token;started.SetResult();await release.Task.WaitAsync(token);return Report();});
        using var pageStop=new CancellationTokenSource();
        var page=analysis.LoadAsync(null,"A",pageStop.Token);await started.Task;
        var overlay=analysis.LoadAsync(null,"A",default);
        pageStop.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>page);
        Assert.False(shared.IsCancellationRequested);
        release.SetResult();Assert.NotNull(await overlay);
    }

    [Fact]
    public async Task LastViewLeavingCancelsUnneededWork()
    {
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken shared=default;
        using var analysis=new PetAnalysisService(async(window,server,token,publish)=>
        {shared=token;started.SetResult();await Task.Delay(Timeout.Infinite,token);return Report();});
        using var stop=new CancellationTokenSource();
        var work=analysis.LoadAsync(null,"A",stop.Token);await started.Task;stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>work);Assert.True(shared.IsCancellationRequested);
    }

    [Fact]
    public async Task ChangedInputsCancelOldWorkAndStartOnlyOneNewCalculation()
    {
        int loads=0;var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken obsolete=default;var final=Report();
        using var analysis=new PetAnalysisService(async(window,server,token,publish)=>
        {
            if(Interlocked.Increment(ref loads)==1){obsolete=token;started.SetResult();await Task.Delay(Timeout.Infinite,token);}
            return final;
        });
        var old=analysis.LoadAsync(null,"A",default);await started.Task;analysis.Invalidate();
        Assert.True(obsolete.IsCancellationRequested);await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>old);
        var page=analysis.LoadAsync(null,"A",default);var overlay=analysis.LoadAsync(null,"A",default);
        Assert.Same(final,await page);Assert.Same(final,await overlay);Assert.Equal(2,loads);
    }

    [Fact]
    public async Task AnotherServerOrCharacterNeverReusesThePreviousReport()
    {
        int loads=0;
        using var analysis=new PetAnalysisService((window,server,token,publish)=>
        {loads++;return Task.FromResult<PetOpportunityReport?>(Report() with {Server=server!});});
        var first=await analysis.LoadAsync(new(1,1),"A",default);
        var otherServer=await analysis.LoadAsync(new(1,1),"B",default);
        var otherCharacter=await analysis.LoadAsync(new(2,2),"B",default);
        Assert.Equal(3,loads);Assert.NotSame(first,otherServer);Assert.NotSame(otherServer,otherCharacter);
    }

    [Fact]
    public async Task SwitchingBetweenDofusAndBestCrushDoesNotRestartTheSameAnalysis()
    {
        int loads=0;var clients=new CurrentDofusClientsState();var key=new DofusClientWindowKey(1,1);
        DofusWindowInfo Window(string name)=>new(new IntPtr(1),$"{name} - Ecaflip - 3.7.14.18 - Release","Dofus",0,0,100,100){ProcessId=1};
        clients.Update([Window("Premier")],key);
        using var analysis=new PetAnalysisService((window,server,token,publish)=>
        {loads++;return Task.FromResult<PetOpportunityReport?>(Report());},clients);
        var first=await analysis.LoadAsync(key,"A",default);
        clients.Update([Window("Premier")],null);clients.Update([Window("Premier")],key);
        Assert.Same(first,await analysis.LoadAsync(key,"A",default));Assert.Equal(1,loads);
        clients.Update([Window("Autre")],key);
        Assert.NotSame(first,await analysis.LoadAsync(key,"A",default));Assert.Equal(2,loads);
    }

    [Fact]
    public void FoodArrowsGoForwardAndBackwardAndStockModeDoesNotEraseInventory()
    {
        var preferences=new PetFoodPreferences();int changes=0;preferences.Changed+=()=>changes++;
        preferences.Skip(10);var first=preferences.Selection;
        preferences.Skip(20);Assert.Equal(2,preferences.Selection.Skipped.Count);
        preferences.Previous();Assert.Equal(new long[]{10},preferences.Selection.Skipped);
        preferences.Previous();Assert.Empty(preferences.Selection.Skipped);preferences.Previous();
        Assert.Contains(10,first.Skipped); // Older snapshots remain immutable during an analysis.
        var inventory=new Dictionary<long,BigInteger>{{10,5},{20,7}};
        preferences.SetUseStocks(false);Assert.Empty(preferences.Selection.PlanningStock(inventory));
        Assert.Equal(5,inventory[10]);
        preferences.SetUseStocks(true);preferences.Skip(10);
        Assert.Equal(new long[]{20},preferences.Selection.PlanningStock(inventory).Keys);
        preferences.Reset();Assert.Empty(preferences.Selection.Skipped);Assert.True(preferences.Selection.UseStocks);
        Assert.Equal(8,changes);
    }
}
