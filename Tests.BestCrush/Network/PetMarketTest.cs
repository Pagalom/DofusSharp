using BestCrush.Network.Protocol;
using BestCrush.Services;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class PetMarketTest
{
    private static byte[] PetEffect(int level) => Message(Unsigned(10,2821),
        Bytes(9,Message(Unsigned(1,18),level == 0 ? [] : Unsigned(4,(ulong)level))));
    private static byte[] Xp(long value) => Message(Unsigned(10,2819),
        Bytes(9,Message(Unsigned(2,12975),value == 0 ? [] : Unsigned(4,(ulong)value))));

    [Fact]
    public void ReadsUnfedAndPartiallyFedStockWithoutLosingNonNumericEffects()
    {
        var zero = PetMarketDecoder.Read([PetEffect(0),Xp(0)],18);
        Assert.Equal(0,zero!.Level); Assert.Equal(0,zero.Experience);
        var feeding = PetMarketDecoder.Read([PetEffect(70),Xp(12526)],18);
        Assert.Equal(70,feeding!.Level); Assert.Equal(12526,feeding.Experience);
        Assert.Equal("normal",feeding.Variant);
        Assert.Null(PetMarketDecoder.Read([PetEffect(70),PetEffect(70),Xp(12526)],18));
        Assert.Null(PetMarketDecoder.Read([PetEffect(70),Xp(12526)],121));
    }

    [Fact]
    public void MarketPreservesLevelXpAndLegendaryVariants()
    {
        byte[] Offer(ulong uid,int level,long xp,long price,params byte[][] extra) =>
            Message(JzsOffer(200,uid,18,(ulong)price,0,0,0),Bytes(5,PetEffect(level)),Bytes(5,Xp(xp)),
                Message(extra.Select(effect => Bytes(5,effect)).ToArray()));
        var snapshot = PetMarketDecoder.Decode(JzsMarket(200,18,
            Offer(1,0,0,1000),Offer(2,70,12526,5000),Offer(3,100,179592,20000,Unsigned(10,2862))));
        Assert.NotNull(snapshot); Assert.Equal(3,snapshot.Offers.Count);
        Assert.Equal([0,70,100],snapshot.Offers.Select(offer=>offer.Level));
        Assert.Equal(12526,snapshot.Offers[1].Experience);
        Assert.True(snapshot.Offers[2].Legendary);
        Assert.NotEqual(snapshot.Offers[0].Variant,snapshot.Offers[2].Variant);
        Assert.Null(PetMarketDecoder.Decode(JzsMarket(200,331,Offer(1,70,12526,5000))));
    }

    [Fact]
    public void FeedingRefreshesTheSameOwnedPetAndDecrementsItsFoodWithoutDuplicatingIt()
    {
        byte[] Item(ulong id,ulong uid,ulong quantity,params byte[][] effects)=>Message(Unsigned(3,quantity),Unsigned(4,id),Unsigned(5,uid),
            Message(effects.Select(effect=>Bytes(7,effect)).ToArray()));
        var book=new LiveStockBook();var at=DateTime.UtcNow;
        book.Observe("irl",Message(Unsigned(4,1),Bytes(2,Bytes(1,Item(100,7,1,PetEffect(0),Xp(0)))),
            Bytes(2,Bytes(1,Item(10,8,10)))),at);
        book.Observe("irx",Bytes(3,Message(Unsigned(1,9),Unsigned(3,8))),at.AddSeconds(1));
        byte[] update=Bytes(1,Message(Bytes(1,Item(100,7,1,PetEffect(23),Xp(101))),Unsigned(4,63)));
        var after=book.Observe("iqy",update,at.AddSeconds(1))!;
        Assert.True(after.Complete);Assert.False(after.Stale);Assert.Equal(2,after.Items.Count);
        var pet=Assert.Single(after.Items,item=>item.ItemId==100);
        var reading=PetMarketDecoder.Read(pet.EffectWire,18)!;
        Assert.Equal(23,reading.Level);Assert.Equal(101,reading.Experience);
        Assert.Equal(9UL,Assert.Single(after.Items,item=>item.ItemId==10).Quantity);
        Assert.Null(LiveStockProtocol.Decode("iqy",Bytes(1,Bytes(1,Item(100,7,1))).Concat(Bytes(1,Bytes(1,Item(100,7,1)))).ToArray()));
    }

    [Fact]
    public void SnapshotReplacementIsServerScopedAndRejectsOlderSnapshots()
    {
        string folder=Path.Combine(Path.GetTempPath(),"pet-market-"+Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            var repo=new GlobalSalesRepository(Path.Combine(folder,"market.db"));
            var at=DateTimeOffset.UtcNow;
            repo.SavePetPrices("A",new(200,[new(70,12526,5000,"normal",false),new(100,179592,10000,"normal",false)]),at);
            repo.SavePetPrices("A",new(200,[new(70,12526,4000,"normal",false)]),at.AddSeconds(1));
            repo.SavePetPrices("A",new(200,[new(100,179592,1,"normal",false)]),at);
            var snapshot=Assert.Single(repo.ReadPetPrices("A",at.AddMinutes(1))).Snapshot;
            Assert.Equal(4000,Assert.Single(snapshot.Offers).Price);
            Assert.Empty(repo.ReadPetPrices("B",at.AddMinutes(1)));
        }
        finally { Directory.Delete(folder,true); }
    }
}
