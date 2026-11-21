using BestCrush.Services;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush;

public sealed class LiveStockServerBindingTest
{
    [Theory]
    [InlineData(1,1,true)]
    [InlineData(1,2,false)]
    [InlineData(2,1,false)]
    public void OnlyTheExactVerifiedGameFlowCanBindAnAnonymousLiveInventory(long leaseEpoch,long leaseConnection,bool binds)
    {
        string folder=Path.Combine(Path.GetTempPath(),"stock-bind-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        try
        {
            var window=new DofusClientWindowKey(111,1111);var clients=new CurrentDofusClientsState();
            clients.Update([new(new IntPtr(111),"Alpha - Ecaflip - 3.7.14.18 - Release","Dofus",0,0,100,100){ProcessId=1111}],window);
            var attribution=new WindowServerAttributionState();attribution.Reset(leaseEpoch);
            var servers=new CurrentServerState(attribution,true);var repository=new LiveStockRepository(Path.Combine(folder,"stock.db"));
            using var stock=new LiveStockService(repository,clients,servers:servers);
            byte[] snapshot=Message(Unsigned(4,1),Bytes(2,Bytes(1,Message(Unsigned(3,1),Unsigned(4,100),Unsigned(5,500)))));
            stock.Observe(new("S→C","irl",snapshot,DateTime.UtcNow,null,1,ConnectionId:1,CandidateWindow:window,SyntheticWindowForReplay:true));
            Assert.Null(stock.ForWindow(window)!.ServerName);
            servers.SelectServer("Draconiros");Assert.Null(stock.ForWindow(window)!.ServerName);
            attribution.ObserveGame(new("S→C","jzs",[],DateTime.UtcNow,null,leaseEpoch,
                ConnectionId:leaseConnection,CandidateWindow:window,SyntheticWindowForReplay:true),true);
            attribution.AssignPlayedServer(clients.SelectedClient!,"Kourial");
            Assert.Equal(binds?"Kourial":null,stock.ForWindow(window)!.ServerName);
            Assert.Equal(binds?"Kourial":null,repository.ReadSources().Single().ServerName);
            Assert.Single(stock.ForWindow(window)!.Containers.Single().Items);
            servers.SelectServer("Brial");Assert.Equal(binds?"Kourial":null,stock.ForWindow(window)!.ServerName);
        }
        finally{Directory.Delete(folder,true);}
    }
}
