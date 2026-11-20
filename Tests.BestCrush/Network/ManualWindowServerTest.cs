using BestCrush.Services;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class ManualWindowServerTest
{
    private static readonly DofusClientWindowKey A=new(111,1111),B=new(222,2222);
    private static DofusClientWindow Client(DofusClientWindowKey key,string name="Alpha")=>new(key,name,"Ecaflip","3.7.14.18",true);
    private static DofusWireMessage Wire(DofusClientWindowKey key,long connection=21,long epoch=1,string opcode="jzs")=>
        new("S→C",opcode,[],DateTime.UtcNow,null,epoch,ConnectionId:connection,CandidateWindow:key,SyntheticWindowForReplay:true);
    private static WindowServerAttributionState Start(){var state=new WindowServerAttributionState();state.Reset(1);return state;}

    [Fact]
    public void DeclarationWorksWithoutReconnectAndSqlBrowsingNeverChangesThePlayedServer()
    {
        var state=Start();var servers=new CurrentServerState(state,true);
        state.ObserveGame(Wire(A),true);
        Assert.Null(servers.GetWindowCaptureLease(A));
        Assert.True(state.AssignPlayedServer(Client(A),"Kourial"));
        var lease=servers.GetWindowCaptureLease(A)!.Value;
        Assert.Equal("Kourial",lease.ServerName);Assert.True(servers.IsCaptureLeaseActive(lease));
        servers.SelectServer("Draconiros");
        Assert.Equal("Draconiros",servers.ServerName);Assert.Equal(lease,servers.GetWindowCaptureLease(A));
        Assert.Null(servers.GetCaptureLease());
        state.ObserveGame(Wire(B,22),true);
        Assert.Null(servers.GetGameCaptureLease(B,1,22));
        Assert.True(state.AssignPlayedServer(Client(B,"Beta"),"Tylezia"));
        Assert.Equal("Tylezia",servers.GetGameCaptureLease(B,1,22)!.Value.ServerName);
        Assert.Equal("Kourial",servers.GetGameCaptureLease(A,1,21)!.Value.ServerName);
        Assert.True(state.AssignPlayedServer(Client(A),"Brial"));
        Assert.False(servers.IsCaptureLeaseActive(lease));
        Assert.Equal("Brial",servers.GetWindowCaptureLease(A)!.Value.ServerName);
    }
    [Fact]
    public void ADeclarationNeedsAnExactlyOwnedLiveGameFlowBeforeItCanWrite()
    {
        var state=Start();Assert.True(state.AssignPlayedServer(Client(A),"Kourial"));
        Assert.Null(state.GetWindowLease(A));
        state.ObserveGame(Wire(A),false);Assert.Null(state.GetGameLease(A,1,21));
        state.ObserveGame(Wire(B),true);Assert.Null(state.GetWindowLease(A));
        state.ObserveGame(Wire(A),true);Assert.NotNull(state.GetGameLease(A,1,21));
        Assert.Null(state.GetGameLease(A,1,22));Assert.Null(state.GetGameLease(A,2,21));
    }
    [Fact]
    public void CharacterChangesClosedWindowsAndNewConnectionsRevokeTheChoice()
    {
        var state=Start();state.ObserveGame(Wire(A),true);state.AssignPlayedServer(Client(A),"Kourial");
        var lease=state.GetWindowLease(A)!.Value;
        state.ReconcileCharacterTitles([Client(A,"Another character")]);
        Assert.False(state.IsGameLeaseActive(lease));Assert.Null(state.ForWindow(A));
        state.ObserveGame(Wire(A),true);state.AssignPlayedServer(Client(A),"Kourial");
        lease=state.GetWindowLease(A)!.Value;
        state.ObserveGame(Wire(A,22),true);
        Assert.False(state.IsGameLeaseActive(lease));Assert.Null(state.ForWindow(A));
        state.AssignPlayedServer(Client(A),"Kourial");
        state.RemoveMissingWindows([B]);Assert.Null(state.ForWindow(A));
        Assert.False(state.AssignPlayedServer(Client(A),"Kourial"));
        state.Reset(2);state.ObserveGame(Wire(A,23,2),true);Assert.True(state.AssignPlayedServer(Client(A),"Kourial"));
        state.Reset(3);Assert.Null(state.ForWindow(A));
    }
    [Fact]
    public void ANewCharacterLoadDropsTheManualChoiceAndGenericTitlesCannotBeAssigned()
    {
        var state=Start();state.ObserveGame(Wire(A),true);state.AssignPlayedServer(Client(A),"Kourial");
        state.ObserveGame(Wire(A,opcode:"ksc"),true);Assert.Null(state.ForWindow(A));
        Assert.False(state.AssignPlayedServer(new(A,"Dofus",null,null,true),"Kourial"));
        Assert.False(state.AssignPlayedServer(Client(A),"not-a-server"));
        Assert.Equal(294,DofusServerIdCatalog.IdFor("HellMina"));
    }
}
