using BestCrush.Network.Protocol;
using BestCrush.Services;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class LiveStockTest
{
    private static readonly DateTime At=new(2026,10,25,14,0,0,DateTimeKind.Utc);
    private static byte[] Item(ulong id,ulong uid,ulong qty)=>Message(Unsigned(3,qty),Unsigned(4,id),Unsigned(5,uid));
    private static byte[] Snapshot(string opcode,params byte[][] rows)=>Message(Unsigned(opcode=="irp"?1:4,1),Message(rows.Select(x=>Bytes(2,Bytes(1,x))).ToArray()));
    private static byte[] Add(string opcode,params byte[][] rows)=>Bytes(opcode=="iqx"?2:1,Message(rows.Select(x=>Bytes(1,x)).ToArray()));
    private static byte[] Qty(ulong uid,ulong qty)=>Bytes(3,Message(Unsigned(1,qty),Unsigned(3,uid)));

    [Fact]
    public void ReceivedEmptyBankReplacesBankWithoutErasingInventoryAndPersistsAsComplete()
    {
        string folder=Path.Combine(Path.GetTempPath(),"empty-bank-"+Guid.NewGuid());
        try
        {
            var book=new LiveStockBook();
            var inventory=book.Observe("irl",Snapshot("irl",Item(10,100,3)),At)!;
            book.Observe("irp",Snapshot("irp",Item(20,200,1)),At);
            // Exact Any shape observed with the never-used bank of the Eniripsa.
            var envelope=AnkamaFrameDecoder.FindAny(Bytes(7,Bytes(1,
                System.Text.Encoding.UTF8.GetBytes("type.ankama.com/irp"))))!;
            var empty=book.Observe(envelope.Key,envelope.Body,At.AddSeconds(1))!;
            Assert.True(empty.Complete);Assert.False(empty.Stale);Assert.Empty(empty.Items);
            Assert.Equal(3UL,Assert.Single(book.Containers.Single(x=>x.Container=="inventory").Items).Quantity);
            var repo=new LiveStockRepository(Path.Combine(folder,"stock.db"));
            repo.Save("eniripsa","Pagalom","Tylezia",inventory);
            repo.Save("eniripsa","Pagalom","Tylezia",empty);
            var source=Assert.Single(repo.ReadSources());
            Assert.True(StockOpportunityService.CanUse(source,"Tylezia"));
            Assert.Empty(source.Containers.Single(x=>x.Container=="bank").Items);
        }
        finally { if(Directory.Exists(folder))Directory.Delete(folder,true); }
    }

    [Fact]
    public void AbsentOrMalformedBankIsExcludedWhileInventoryCanStillBeUsed()
    {
        var book=new LiveStockBook();
        book.Observe("irl",Snapshot("irl",Item(10,100,3)),At);
        var source=new LiveStockSource("s","Pagalom","Tylezia",At,book.Containers);
        Assert.True(StockOpportunityService.CanUse(source,"Tylezia"));
        Assert.False(StockOpportunityService.HasSynchronizedBank(source));
        Assert.Null(book.Observe("unrecognized",[],At.AddSeconds(1)));
        var malformed=book.Observe("irp",[0],At.AddSeconds(2))!;
        Assert.False(malformed.Complete);Assert.True(malformed.Stale);
        Assert.True(StockOpportunityService.CanUse(source with{Containers=book.Containers},"Tylezia"));
        Assert.False(StockOpportunityService.HasSynchronizedBank(source with{Containers=book.Containers}));
    }

    [Fact]
    public void LootIntoExistingStackUsesAbsoluteQuantityAndDuplicateIsIdempotent()
    {
        var book=new LiveStockBook();
        book.Observe("irl",Snapshot("irl",Item(10,100,3)),At);
        book.Observe("irx",Qty(100,8),At.AddSeconds(1));
        var result=book.Observe("irx",Qty(100,8),At.AddSeconds(2))!;
        Assert.True(result.Complete);Assert.False(result.Stale);
        Assert.Equal(8UL,Assert.Single(result.Items).Quantity);
    }

    [Fact]
    public void NewLootIsUpsertedWithAllEffectsAndNeverDoubleCounted()
    {
        var book=new LiveStockBook();
        book.Observe("irl",Snapshot("irl"),At);
        byte[] effect=Bytes(7,Message(Bytes(9,[8,4]),Unsigned(10,112)));
        byte[] add=Add("iqx",Message(Item(10,100,7),effect));
        book.Observe("iqx",add,At.AddSeconds(1));
        var result=book.Observe("iqx",add,At.AddSeconds(2))!;
        var row=Assert.Single(result.Items);
        Assert.Equal(7UL,row.Quantity);Assert.Null(row.Effects);Assert.NotNull(row.EffectWire);
        Assert.Single(row.EffectWire!);Assert.True(result.Complete);
    }

    [Fact]
    public void BothSidesOfBankTransferAndRemovalRemainIndependent()
    {
        var book=new LiveStockBook();
        book.Observe("irl",Snapshot("irl",Item(10,100,3)),At);
        book.Observe("irp",Snapshot("irp",Item(10,200,6)),At);
        book.Observe("irx",Qty(100,4),At.AddSeconds(1));
        book.Observe("iqz",Add("iqz",Item(10,200,5)),At.AddSeconds(1));
        Assert.Equal(4UL,book.Containers.Single(x=>x.Container=="inventory").Items[0].Quantity);
        Assert.Equal(5UL,book.Containers.Single(x=>x.Container=="bank").Items[0].Quantity);
        book.Observe("itf",Unsigned(1,200),At.AddSeconds(2));
        Assert.Empty(book.Containers.Single(x=>x.Container=="bank").Items);
        book.Observe("iqm",Unsigned(1,100),At.AddSeconds(2));
        Assert.Empty(book.Containers.Single(x=>x.Container=="inventory").Items);
    }

    [Fact]
    public void UnknownQuantityInvalidatesUntilFullResnapshotAndDoesNotInventItem()
    {
        var book=new LiveStockBook();
        book.Observe("irl",Snapshot("irl",Item(10,100,3)),At);
        var bad=book.Observe("irx",Qty(999,8),At.AddSeconds(1))!;
        Assert.True(bad.Stale);Assert.Single(bad.Items);
        var reset=book.Observe("irl",Snapshot("irl",Item(10,100,4)),At.AddSeconds(2))!;
        Assert.False(reset.Stale);Assert.True(reset.Complete);Assert.Equal(4UL,reset.Items[0].Quantity);
    }

    [Fact]
    public void UpdatesBeforeSnapshotArePartialAndMalformedUpdatesCannotEraseStock()
    {
        var book=new LiveStockBook();
        var partial=book.Observe("iqx",Add("iqx",Item(10,100,2)),At)!;
        Assert.False(partial.Complete);
        var bad=book.Observe("iqm",[0],At.AddSeconds(1))!;
        Assert.True(bad.Stale);Assert.Single(bad.Items);
        var empty=book.Observe("irl",Snapshot("irl"),At.AddSeconds(2))!;
        Assert.True(empty.Complete);Assert.False(empty.Stale);Assert.Empty(empty.Items);
    }

    [Fact]
    public void QuantityZeroDeletesAndOlderEventsAreRejected()
    {
        var book=new LiveStockBook();
        book.Observe("irl",Snapshot("irl",Item(10,100,3)),At);
        book.Observe("irx",Qty(100,7),At.AddSeconds(2));
        Assert.Null(book.Observe("irx",Qty(100,2),At.AddSeconds(1)));
        Assert.Empty(book.Observe("irx",Qty(100,0),At.AddSeconds(3))!.Items);
    }

    [Fact]
    public void DuplicateOrMalformedScalarsAreRejected()
    {
        Assert.Null(LiveStockProtocol.Decode("irx",Bytes(3,Message(Unsigned(1,2),Unsigned(1,3),Unsigned(3,100)))));
        Assert.Null(LiveStockProtocol.Decode("iqm",Message(Unsigned(1,100),Unsigned(1,101))));
        Assert.Null(LiveStockProtocol.Decode("iqx",Add("iqx",Item(10,100,1),Item(11,100,2))));
    }

    [Fact]
    public void RepositoryPersistsUnknownEffectsAndReplacesOneContainerAtomically()
    {
        string folder=Path.Combine(Path.GetTempPath(),"live-stock-"+Guid.NewGuid());
        try
        {
            var repo=new LiveStockRepository(Path.Combine(folder,"stock.db"));
            var book=new LiveStockBook();
            repo.Save("source-a","Fenêtre A","Kourial",book.Observe("irl",Snapshot("irl",Item(10,100,2)),At)!);
            repo.Save("source-a","Fenêtre A",null,book.Observe("irp",Snapshot("irp",Item(20,200,3)),At)!);
            repo.Save("source-a","Fenêtre A",null,book.Observe("irx",Qty(100,4),At.AddSeconds(1))!);
            repo.Save("source-b","Fenêtre B","Draconiros",new("inventory",true,false,At,[]));
            var reloaded=new LiveStockRepository(Path.Combine(folder,"stock.db")).ReadSources();
            Assert.Equal(2,reloaded.Count);
            var first=reloaded.Single(x=>x.Id=="source-a");
            Assert.Equal("Kourial",first.ServerName);Assert.Equal(2,first.Containers.Count);
            Assert.Equal(4UL,first.Containers.Single(x=>x.Container=="inventory").Items[0].Quantity);
        }
        finally { if(Directory.Exists(folder))Directory.Delete(folder,true); }
    }
}
