using BestCrush.Network.Protocol;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class PetFeedingTrackerTest
{
    private static readonly DateTime At=DateTime.UtcNow;
    internal static byte[] Pet(int level,long xp,ulong uid=7)=>Message(Unsigned(3,1),Unsigned(4,12133),Unsigned(5,uid),
        Bytes(7,Message(Unsigned(10,2821),Bytes(9,Message(Unsigned(1,18),Unsigned(4,(ulong)level))))),
        Bytes(7,Message(Unsigned(10,2819),Bytes(9,Unsigned(4,(ulong)xp)))));
    internal static byte[] Snapshot(ulong food=10)=>Message(Unsigned(4,1),Bytes(2,Bytes(1,Pet(0,0))),
        Bytes(2,Bytes(1,Message(Unsigned(3,food),Unsigned(4,13942),Unsigned(5,8)))));
    internal static byte[] Request(int quantity=1)=>Message(Unsigned(2,7),Bytes(3,Message(Unsigned(1,(ulong)quantity),Unsigned(3,8))));
    internal static byte[] Quantity(ulong remaining)=>Bytes(3,Message(Unsigned(1,remaining),Unsigned(3,8)));
    internal static byte[] UpdatedPet(int level=23,long xp=101)=>Bytes(1,Message(Bytes(1,Pet(level,xp)),Unsigned(4,63)));
    internal static PetFeedingTracker Start(ulong food=10)
    {var t=new PetFeedingTracker();t.Observe("S→C","irl",Snapshot(food),At);return t;}

    [Theory]
    [InlineData(false,1,101,23)]
    [InlineData(true,1,101,23)]
    [InlineData(false,10,1012,44)]
    [InlineData(false,1,0,0)]
    public void LearnsOnlyAfterBothConsumptionAndPetXpAreConfirmed(bool reversed,int quantity,int xp,int level)
    {
        var t=Start(20);t.Observe("C→S","irb",Request(quantity),At);
        string first=reversed ? "iqy":"irx",second=reversed ? "irx":"iqy";
        byte[] body(string key)=>key=="irx" ? Quantity((ulong)(20-quantity)) : UpdatedPet(level,xp);
        Assert.Null(t.Observe("S→C",first,body(first),At));
        var meal=t.Observe("S→C",second,body(second),At)!;
        Assert.NotNull(meal);Assert.Equal(quantity,meal.Quantity);Assert.Equal(xp,meal.ExperienceAfter);Assert.Equal(13942,meal.FoodItemId);
        Assert.Null(t.Observe("S→C",second,body(second),At));
    }
    [Fact]
    public void ExpiredMixedCappedOrMismatchedMealsNeverBecomeReferenceXp()
    {
        foreach(string invalid in new[]{"expired","mixed","capped","quantity","snapshot","unknown-pet"})
        {
            var t=Start();byte[] request=invalid=="mixed" ? Message(Request(),Bytes(3,Message(Unsigned(1,1),Unsigned(3,9)))) : Request();
            if(invalid=="unknown-pet") request=Message(Unsigned(2,99),Bytes(3,Message(Unsigned(1,1),Unsigned(3,8))));
            t.Observe("C→S","irb",request,At);
            var after=invalid=="expired" ? At.AddSeconds(11) : At;
            if(invalid=="snapshot") t.Observe("S→C","irl",Snapshot(),after);
            Assert.Null(t.Observe("S→C","irx",Quantity(invalid=="quantity" ? 8UL : 9UL),after));
            Assert.Null(t.Observe("S→C","iqy",invalid=="capped" ? UpdatedPet(100,179592) : UpdatedPet(),after));
        }
    }
    [Fact]
    public void WholeStackRemovalAndRepeatedMealsRemainDistinct()
    {
        var t=Start(1);t.Observe("C→S","irb",Request(),At);
        t.Observe("S→C","iqm",Unsigned(1,8),At);
        Assert.NotNull(t.Observe("S→C","iqy",UpdatedPet(),At));
        Assert.Null(t.Observe("S→C","iqy",UpdatedPet(),At));
        Assert.Null(PetFeedingTracker.DecodeRequest(Message(Request(),Unsigned(2,7))));
    }
}
