namespace BestCrush.Network.Protocol;

public sealed record PetFeedingRequest(ulong PetUid,ulong FoodUid,int Quantity);
public sealed record PetFeedingObservation(long Sequence,long FoodItemId,long PetItemId,int Quantity,
    long ExperienceBefore,long ExperienceAfter,DateTime ObservedAtUtc);

/// <summary>3.7.14.18 irb request, confirmed by irx/iqm consumption and an iqy pet update.</summary>
public sealed class PetFeedingTracker
{
    private readonly LiveStockBook _stock=new();
    private sealed record Pending(long Sequence,PetFeedingRequest Request,LiveStockItem Food,LiveStockItem Pet,
        PetItemReading Reading,DateTime At)
    {
        internal bool Consumed;
        internal PetItemReading? After;
    }
    private Pending? _pending;
    private long _sequence;
    public static PetFeedingRequest? DecodeRequest(byte[] body)
    {
        if(body.Length is 0 or >1024 || ProtoWire.ReadFields(body) is not { } fields ||
            Scalar(fields,2) is not >0 || fields.Where(f=>f.Number==3).ToArray() is not [var meal] ||
            meal.WireType!=ProtoWireType.LengthDelimited || meal.Bytes is null || ProtoWire.ReadFields(meal.Bytes) is not { } food ||
            Scalar(food,1) is not (>0 and <=int.MaxValue) || Scalar(food,3) is not >0) return null;
        ulong pet=Scalar(fields,2)!.Value,uid=Scalar(food,3)!.Value;
        return pet==uid ? null : new(pet,uid,(int)Scalar(food,1)!.Value);
    }
    private static ulong? Scalar(List<ProtoField> fields,int number)=>
        fields.Where(f=>f.Number==number).ToArray() is [{WireType:ProtoWireType.Varint} f] ? f.Varint : null;
    private static PetItemReading? Pet(LiveStockItem item)=>PetMarketDecoder.Read(item.EffectWire,18) ?? PetMarketDecoder.Read(item.EffectWire,121);
    public PetFeedingObservation? Observe(string direction,string key,byte[] body,DateTime at)
    {
        if(_pending is { } waiting && (at<waiting.At || at-waiting.At>TimeSpan.FromSeconds(10))) _pending=null;
        if(direction=="C→S" && key=="irb")
        {
            _sequence++;
            if(_pending is not null){_pending=null;return null;}
            var inventory=_stock.Containers.FirstOrDefault(c=>c.Container=="inventory");
            if(DecodeRequest(body) is not { } request || inventory is not {Complete:true,Stale:false}) return null;
            var pet=inventory.Items.FirstOrDefault(i=>i.Uid==request.PetUid);
            var food=inventory.Items.FirstOrDefault(i=>i.Uid==request.FoodUid);
            if(pet is not {Quantity:1,ItemId:<=long.MaxValue} || food is not {ItemId:<=long.MaxValue} || food.Quantity<(ulong)request.Quantity ||
                Pet(pet) is not {Level:<100,Experience:{}} reading) return null;
            _pending=new(_sequence,request,food,pet,reading,at);
            return null;
        }
        if(direction!="S→C" || !LiveStockProtocol.IsStockOpcode(key)) return null;
        var current=_stock.Observe(key,body,at);
        if(_pending is not { } pending) return null;
        if(current is {Container:"inventory"} && (current.Stale || key is "irl" or "iqx")) {_pending=null;return null;}
        if(key is "irx" or "iqm" && LiveStockProtocol.Decode(key,body) is { } change && change.Uid==pending.Food.Uid)
        {
            ulong after=change.Operation==LiveStockOperation.Remove ? 0 : change.Quantity;
            if(after!=pending.Food.Quantity-(ulong)pending.Request.Quantity){_pending=null;return null;}
            pending.Consumed=true;
        }
        if(key=="iqy" && LiveStockProtocol.Decode(key,body)?.Items.SingleOrDefault() is { } item && item.Uid==pending.Pet.Uid)
        {
            var after=Pet(item);
            if(item.ItemId!=pending.Pet.ItemId || item.Quantity!=1 || after is not {Level:<100,Experience:{}} ||
                after.Variant!=pending.Reading.Variant || after.Experience<pending.Reading.Experience)
            {_pending=null;return null;}
            pending.After=after;
        }
        if(!pending.Consumed || pending.After?.Experience is not { } xp) return null;
        _pending=null;
        return new(pending.Sequence,(long)pending.Food.ItemId,(long)pending.Pet.ItemId,pending.Request.Quantity,
            pending.Reading.Experience!.Value,xp,at);
    }
}
