using System.Text.Json;
using BestCrush.Domain.Models;
using BestCrush.Services;

namespace Tests.BestCrush;

public sealed class ForgemagingWorkshopReplayTest
{
    private sealed record Replay(string Direction, string Key, string BodyBase64);
    private static Replay[] Read() => JsonSerializer.Deserialize<Replay[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "NetworkFixtures", "fm-live-3.7.13.17.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    private static readonly DateTime At = new(2026,11,5,0,0,0,DateTimeKind.Utc);
    private static byte[] V(int field,ulong value) { List<byte> bytes=[(byte)(field*8)];do{bytes.Add((byte)((value&127)|(value>=128?128UL:0)));value>>=7;}while(value>0);return bytes.ToArray(); }
    private static byte[] B(int field,byte[] bytes)=>[(byte)(field*8+2),(byte)bytes.Length,..bytes];
    private static byte[] Item(ulong uid,long id,int vitality,int? chance=null)=>[..V(3,1),..V(4,(ulong)id),..V(5,uid),
        ..B(7,[..V(5,(ulong)vitality),..V(10,125)]),..(chance is {} c?B(7,[..V(5,(ulong)c),..V(10,123)]):[])];
    private static byte[] RunePacket(ulong uid,long id,int gain)=>[21,0,0,0,0,..B(3,[..B(1,Item(uid,id,gain)),..V(4,63)])];
    private static byte[] Request(ulong uid)=>[..V(1,uid),..V(2,1),..V(4,1)];
    private static byte[] Quantity(ulong uid,ulong quantity)=>B(3,[..V(1,quantity),..V(3,uid)]);
    private static byte[] Success(int vitality,ulong uid=1111111)=>[..V(1,2),..B(2,[..V(3,0),..B(4,Item(uid,8538,vitality,5))])];
    private static Dictionary<Characteristic,int>? Map(ForgemagingWireItem item)
    {
        Dictionary<Characteristic,int> result = [];
        foreach (var effect in item.Effects)
        {
            Characteristic? stat = effect.Code switch { 125 => Characteristic.Vitality, 123 => Characteristic.Chance, _ => null };
            if (stat is null || effect.Value is null) return null;
            result[stat.Value] = checked((int)effect.Value.Value);
        }
        return result;
    }
    private static (ForgemagingSession Session,ForgemagingWorkshopTracker Tracker) Create()
    {
        var baseline = LiveForgemagingProtocol.WorkshopItem(Convert.FromBase64String(Read()[1].BodyBase64))!.Value;
        var session = new ForgemagingSession { ItemId = 8538, ItemName = "Test", Level = 1, GameRecordingId = "test", Sink = 0,
            Lines = [new() { Characteristic = Characteristic.Chance, Current = 5, Minimum = 4, Maximum = 6, Target = 6 },
                     new() { Characteristic = Characteristic.Vitality, Current = 15, Minimum = 7, Maximum = 10, Target = 10 }] };
        return (session,new(session,baseline.Item,[new(1523,"Rune Vi",Characteristic.Vitality,5,245)],Map));
    }

    [Fact]
    public void SixRealTrialsMatchJetsOutcomesAbsolutePoolsAndConsumedRunes()
    {
        // Only UIDs were anonymized; result bytes, float precision and duplicate item updates are preserved.
        var (session,tracker) = Create();
        foreach (var row in Read()) tracker.Observe(row.Direction,row.Key,Convert.FromBase64String(row.BodyBase64),At);
        Assert.Equal(new[] {ForgemagingOutcome.Critical,ForgemagingOutcome.Critical,ForgemagingOutcome.Neutral,
            ForgemagingOutcome.Critical,ForgemagingOutcome.Failure,ForgemagingOutcome.Neutral},session.Attempts.Select(a=>a.Outcome));
        Assert.Equal(6,session.Attempts.Sum(a=>a.RuneCount)); Assert.Equal(1470,session.KnownSpent);
        Assert.Equal(39,session.Lines.Single(l=>l.Characteristic==Characteristic.Vitality).Current);
        Assert.Equal(2,session.Lines.Single(l=>l.Characteristic==Characteristic.Chance).Current); Assert.Equal(0.2,session.Sink);
        Assert.Equal(35,session.Attempts[4].Before.Single(l=>l.Characteristic==Characteristic.Vitality).Current);
        Assert.Equal(34,session.Attempts[4].After.Single(l=>l.Characteristic==Characteristic.Vitality).Current);
        Assert.All(session.Attempts,a=> {Assert.True(a.FromGame);Assert.NotNull(a.PredictedOdds);});
        Assert.DoesNotContain("Uid",JsonSerializer.Serialize(session));
        Assert.Equal(6,session.Attempts.Select(a=>a.ObservationId).Distinct().Count());
    }

    [Fact]
    public void DuplicateResultAndQuantityPacketsDoNotDoubleCountButSubsequentTrialsStillCount()
    {
        var (session,tracker) = Create();
        foreach (var row in Read())
        {
            var bytes=Convert.FromBase64String(row.BodyBase64);
            tracker.Observe(row.Direction,row.Key,bytes,At);
            if (row.Key is "irx" or "kco") tracker.Observe(row.Direction,row.Key,bytes,At);
        }
        Assert.Equal(6,session.Attempts.Count);Assert.Equal(1470,session.KnownSpent);
    }

    [Fact]
    public void AResultWithoutConfirmedConsumptionCannotBecomeAPaidAttempt()
    {
        var (session,tracker)=Create();
        foreach (var row in Read().Skip(2).Take(4).Where(r=>r.Key!="irx"))
            tracker.Observe(row.Direction,row.Key,Convert.FromBase64String(row.BodyBase64),At);
        Assert.Empty(session.Attempts);Assert.Equal(0,session.KnownSpent);
        Assert.All(session.Lines,l=>Assert.Null(l.Current));Assert.Null(session.Sink);
    }

    [Fact]
    public void AGapOfTwoRunesIsNotSilentlyRecordedAsOneAttempt()
    {
        var (session,tracker)=Create();var rows=Read();
        foreach(var row in rows.Take(8))tracker.Observe(row.Direction,row.Key,Convert.FromBase64String(row.BodyBase64),At);
        // The next observed quantity is 34 instead of 35: one result was missed.
        var request=rows.First(r=>r.Key=="jxu");var rune=rows.Skip(2).First(r=>r.Key=="kch");
        tracker.Observe(request.Direction,request.Key,Convert.FromBase64String(request.BodyBase64),At);
        tracker.Observe(rune.Direction,rune.Key,Convert.FromBase64String(rune.BodyBase64),At);
        var quantity=rows.Where(r=>r.Key=="irx").ElementAt(2);
        Assert.Equal(ForgemagingTrackingUpdate.Gap,tracker.Observe(quantity.Direction,quantity.Key,Convert.FromBase64String(quantity.BodyBase64),At));
        Assert.Single(session.Attempts);Assert.All(session.Lines,l=>Assert.Null(l.Current));
    }

    [Fact]
    public void SuccessWithoutVisibleLossStillNeedsPoolConfirmation()
    {
        var rune=new ForgemagingRune(1,"Rune Vi",Characteristic.Vitality,5,1);
        Dictionary<Characteristic,int> before=new(){[Characteristic.Vitality]=10},after=new(){[Characteristic.Vitality]=15};
        Assert.Equal(ForgemagingOutcome.Unconfirmed,ForgemagingWorkshopTracker.Classify(new(2,null,null,null),rune,before,after,null));
        Assert.Equal(ForgemagingOutcome.Neutral,ForgemagingWorkshopTracker.Classify(new(2,null,0,2),rune,before,after,1));
        Assert.Equal(ForgemagingOutcome.Critical,ForgemagingWorkshopTracker.Classify(new(2,null,1,0),rune,before,after,1));
    }

    [Fact]
    public void PaAndRaVariantsAreRecognizedByTheirActualRuneIdsAndGains()
    {
        var (session,tracker)=Create();
        tracker.RefreshRunes([new(5001,"Rune Pa Vi",Characteristic.Vitality,15,13),new(5002,"Rune Ra Vi",Characteristic.Vitality,50,29)]);
        foreach(var sample in new[]{(Uid:101UL,Id:5001L,Gain:15,After:30),(Uid:102UL,Id:5002L,Gain:50,After:80)})
        {
            tracker.Observe("C→S","jxu",Request(sample.Uid),At);
            tracker.Observe("S→C","kch",RunePacket(sample.Uid,sample.Id,sample.Gain),At);
            tracker.Observe("S→C","irx",Quantity(sample.Uid,4),At);
            Assert.Equal(ForgemagingTrackingUpdate.Recorded,tracker.Observe("S→C","kco",Success(sample.After),At));
        }
        Assert.Equal(new[]{"Rune Pa Vi","Rune Ra Vi"},session.Attempts.Select(a=>a.Rune.Name));
        Assert.Equal(42,session.KnownSpent);Assert.Equal(80,session.Lines.Single(l=>l.Characteristic==Characteristic.Vitality).Current);
    }

    [Fact]
    public void RepeatedUnchangedFailuresInASeriesEachRequireANewConsumption()
    {
        var (session,tracker)=Create();
        tracker.Observe("C→S","jxu",Request(2222222),At);
        tracker.Observe("S→C","kch",RunePacket(2222222,1523,5),At);
        foreach(ulong quantity in new ulong[]{2,1})
        {
            tracker.Observe("S→C","irx",Quantity(2222222,quantity),At);
            tracker.Observe("S→C","kco",V(1,3),At);
        }
        tracker.Observe("S→C","iqm",V(1,2222222),At);
        tracker.Observe("S→C","kco",V(1,3),At);
        tracker.Observe("S→C","kco",V(1,3),At);
        Assert.Equal(3,session.Attempts.Count);Assert.Equal(735,session.KnownSpent);
        Assert.All(session.Attempts,a=>Assert.Equal(ForgemagingOutcome.Failure,a.Outcome));
        Assert.Equal(15,session.Lines.Single(l=>l.Characteristic==Characteristic.Vitality).Current);
    }

    [Fact]
    public void AResultForADifferentEquipmentCannotBeAttachedToTheCurrentSession()
    {
        var(session,tracker)=Create();
        tracker.Observe("C→S","jxu",Request(2222222),At);
        tracker.Observe("S→C","kch",RunePacket(2222222,1523,5),At);
        tracker.Observe("S→C","irx",Quantity(2222222,36),At);
        Assert.Equal(ForgemagingTrackingUpdate.Gap,tracker.Observe("S→C","kco",Success(20,999999),At));
        Assert.Empty(session.Attempts);Assert.All(session.Lines,l=>Assert.Null(l.Current));
    }

    [Fact]
    public void RefreshingPricesOnlyChangesTheCostOfFutureAttempts()
    {
        var(session,tracker)=Create();var rows=Read();
        foreach(var row in rows.Take(8))tracker.Observe(row.Direction,row.Key,Convert.FromBase64String(row.BodyBase64),At);
        tracker.RefreshRunes([new(1523,"Rune Vi",Characteristic.Vitality,5,500)]);
        foreach(var row in rows.Skip(8).Take(6))tracker.Observe(row.Direction,row.Key,Convert.FromBase64String(row.BodyBase64),At);
        Assert.Equal(new double?[]{245,500},session.Attempts.Select(a=>a.Rune.UnitPrice));Assert.Equal(745,session.KnownSpent);
    }
}
