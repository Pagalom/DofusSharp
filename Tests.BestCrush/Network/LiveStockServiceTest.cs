using BestCrush.Network.Protocol;
using BestCrush.Services;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class LiveStockServiceTest : IDisposable
{
    private readonly string _folder=Path.Combine(Path.GetTempPath(),"stock-service-"+Guid.NewGuid());
    private static readonly DateTime At=new(2026,10,25,14,0,0,DateTimeKind.Utc);
    private static readonly DofusClientWindowKey A=new(111,1111),B=new(222,2222);
    private static byte[] Snapshot(string opcode,ulong qty=3)=>Message(Unsigned(opcode=="irp"?1:4,1),
        Bytes(2,Bytes(1,Message(Unsigned(3,qty),Unsigned(4,391),Unsigned(5,100)))));
    private static DofusWireMessage Wire(string opcode,byte[] body,DofusClientWindowKey? window,long connection=1,
        string direction="S→C",string? server="Kourial")=>new(direction,opcode,body,At,
            server is null?null:new NetworkCaptureLease(server,1),3,ConnectionId:connection,CandidateWindow:window,SyntheticWindowForReplay:true);
    private (LiveStockService Stock,CurrentDofusClientsState Clients,LiveStockRepository Repository) Create(bool withBindings=false)
    {
        var clients=new CurrentDofusClientsState();
        clients.Update([
            new DofusWindowInfo(new IntPtr(A.Handle),"A - Ecaflip - 3.7.12.12 - Release","Dofus",0,0,100,100){ProcessId=A.ProcessId},
            new DofusWindowInfo(new IntPtr(B.Handle),"B - Ecaflip - 3.7.12.12 - Release","Dofus",0,0,100,100){ProcessId=B.ProcessId}],A);
        var repository=new LiveStockRepository(Path.Combine(_folder,Guid.NewGuid()+".db"));
        return(new LiveStockService(repository,clients,withBindings?new LiveDofusClientNetworkBindings():null),clients,repository);
    }
    [Fact]
    public void WindowsAndServersStayIsolatedDespiteSameUidAndItem()
    {
        var (stock,clients,_)=Create();
        stock.Observe(Wire("irl",Snapshot("irl",3),A));
        stock.Observe(Wire("irp",Snapshot("irp",6),A));
        stock.Observe(Wire("irl",Snapshot("irl",90),B,2,server:"Draconiros"));
        Assert.Equal(3,stock.Quantity(391,"inventory","Kourial"));
        Assert.Equal(6,stock.Quantity(391,"bank","Kourial"));
        Assert.Null(stock.Quantity(391,"inventory","Draconiros"));
        clients.SelectWindow(B);
        Assert.Equal(90,stock.Quantity(391,"inventory","Draconiros"));
        Assert.Null(stock.Quantity(391,"bank","Draconiros"));
    }
    [Fact]
    public void NewConnectionDoesNotInheritBankOrSwitchBackToOlderFlow()
    {
        var (stock,_,_)=Create();
        stock.Observe(Wire("irl",Snapshot("irl",3),A));
        stock.Observe(Wire("irp",Snapshot("irp",6),A));
        stock.Observe(Wire("irl",Snapshot("irl",8),A,2));
        Assert.Equal(8,stock.Quantity(391,"inventory"));Assert.Null(stock.Quantity(391,"bank"));
        stock.Observe(Wire("irp",Snapshot("irp",9),A,1));
        Assert.Equal(8,stock.Quantity(391,"inventory"));Assert.Null(stock.Quantity(391,"bank"));
    }
    [Fact]
    public void MissingOwnerAndClientMessagesNeverProduceStock()
    {
        var (stock,_,repo)=Create();
        stock.Observe(Wire("irl",Snapshot("irl"),null));
        stock.Observe(Wire("irl",Snapshot("irl"),A,direction:"C→S"));
        Assert.Null(stock.Selected);Assert.Empty(repo.ReadSources());
    }
    [Fact]
    public void ClearingLiveViewPreservesArchiveButNewProcessDoesNotPretendItIsCurrent()
    {
        var (stock,clients,repo)=Create();
        stock.Observe(Wire("irl",Snapshot("irl"),A));
        stock.ClearLive();Assert.Null(stock.Selected);Assert.Single(stock.History);
        var next=new LiveStockService(repo,clients);
        Assert.Null(next.Selected);Assert.Single(next.History);
    }
    [Fact]
    public void NoLongerVerifiedOsConnectionCannotSupplyCurrentQuantities()
    {
        var (stock,_,repo)=Create(withBindings:true);
        stock.Observe(Wire("irl",Snapshot("irl"),A));
        Assert.Single(repo.ReadSources());Assert.Null(stock.Selected);Assert.Null(stock.Quantity(391,"inventory"));
    }
    public void Dispose(){if(Directory.Exists(_folder))Directory.Delete(_folder,true);}
}
