using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;

namespace BestCrush.Services;

internal enum ForgemagingTrackingUpdate { Ignored, Recorded, Gap }

/// <summary>One verified workshop/item stream. Inventory decrements alone never create attempts.</summary>
internal sealed class ForgemagingWorkshopTracker(ForgemagingSession session, ForgemagingWireItem initial,
    IReadOnlyList<ForgemagingRune> runes, Func<ForgemagingWireItem, Dictionary<Characteristic,int>?> map)
{
    private ForgemagingWireItem? _before = initial;
    private readonly ulong _targetUid = initial.Uid;
    private readonly long _itemId = initial.ItemId;
    private IReadOnlyList<ForgemagingRune> _runes = runes;
    private readonly string _segment = Guid.NewGuid().ToString("N");
    private ulong _runeUid;
    private ulong? _remaining;
    private ForgemagingRune? _rune;
    private bool _request, _consumed;
    private long _sequence;
    internal void RefreshRunes(IReadOnlyList<ForgemagingRune> values) => _runes = values;
    internal void Invalidate()
    {
        _before = null; _rune = null; _request = _consumed = false;
        session.Sink = null; foreach (var line in session.Lines) line.Current = null;
    }
    private ForgemagingTrackingUpdate Gap() { Invalidate(); return ForgemagingTrackingUpdate.Gap; }

    internal ForgemagingTrackingUpdate Observe(string direction, string key, byte[] body, DateTime atUtc)
    {
        if (_before is null) return ForgemagingTrackingUpdate.Ignored;
        if (direction == "C→S")
        {
            if (key != "jxu") return ForgemagingTrackingUpdate.Ignored;
            var fields = LiveForgemagingProtocol.Fields(body);
            ulong? uid = LiveForgemagingProtocol.Scalar(fields, 1);
            // A single confirmed result is one rune, even when the client later repeats the action.
            if (uid is not > 0 || LiveForgemagingProtocol.Scalar(fields, 2) != 1 || _consumed) return Gap();
            if (_runeUid != uid) { _rune = null; _remaining = null; }
            else if (_rune is not null) _rune = _runes.FirstOrDefault(r => r.Id == _rune.Id);
            _runeUid = uid.Value; _request = true;
            return ForgemagingTrackingUpdate.Ignored;
        }
        if (direction != "S→C") return ForgemagingTrackingUpdate.Ignored;
        if (key == "kch")
        {
            if (LiveForgemagingProtocol.WorkshopItem(body) is not { } description || description.Item.Uid != _runeUid) return ForgemagingTrackingUpdate.Ignored;
            var candidate = _runes.FirstOrDefault(r => r.Id == description.Item.ItemId);
            var values = map(description.Item);
            if (candidate is null || values is null || values.Count != 1 || values.GetValueOrDefault(candidate.Characteristic) != candidate.Gain) return Gap();
            _rune = candidate; return ForgemagingTrackingUpdate.Ignored;
        }
        if (key is "irx" or "iqm")
        {
            var mutation = LiveStockProtocol.Decode(key, body);
            if (mutation is null || mutation.Uid != _runeUid || _rune is null) return ForgemagingTrackingUpdate.Ignored;
            ulong quantity = key == "iqm" ? 0 : mutation.Quantity;
            if (_remaining is { } old)
            {
                if (quantity == old) return ForgemagingTrackingUpdate.Ignored;
                if (old == 0 || quantity > old || old - quantity != 1) return Gap();
            }
            else if (!_request) return Gap();
            if (_consumed) return Gap();
            _remaining = quantity; _consumed = true; _sequence++;
            return ForgemagingTrackingUpdate.Ignored;
        }
        if (key != "kco") return ForgemagingTrackingUpdate.Ignored;
        if (!_consumed) return _request ? Gap() : ForgemagingTrackingUpdate.Ignored;
        var result = LiveForgemagingProtocol.Result(body);
        if (result is null || _rune is null || (result.Item is { } item && (item.Uid != _targetUid || item.ItemId != _itemId))) return Gap();
        var before = map(_before); var after = result.Item is { } next ? map(next) : before;
        if (before is null || after is null) return Gap();
        double? sink = result.Item is null ? session.Sink : result.Sink;
        var outcome = Classify(result, _rune, before, after, session.Sink);
        ForgemagingSessionActions.RecordObserved(session, _rune, outcome, before, after,
            $"{_segment}:{_sequence}", atUtc, sink, result.Code);
        _before = result.Item ?? _before; _consumed = _request = false;
        return ForgemagingTrackingUpdate.Recorded;
    }

    internal static ForgemagingOutcome Classify(ForgemagingWireResult result, ForgemagingRune rune,
        IReadOnlyDictionary<Characteristic,int> before, IReadOnlyDictionary<Characteristic,int> after, double? sinkBefore)
    {
        var stats = before.Keys.Union(after.Keys).ToArray();
        int Delta(Characteristic stat) => after.GetValueOrDefault(stat) - before.GetValueOrDefault(stat);
        if (result.Code is 1 or 3) return stats.Any(s => Delta(s) > 0) ? ForgemagingOutcome.Unconfirmed : ForgemagingOutcome.Failure;
        if (result.Code != 2 || stats.Any(s => s != rune.Characteristic && Delta(s) > 0) || Delta(rune.Characteristic) > rune.Gain)
            return ForgemagingOutcome.Unconfirmed;
        if (stats.Any(s => Delta(s) < 0) || Delta(rune.Characteristic) < rune.Gain || result.PoolStatus is 1 or 2 ||
            sinkBefore is { } previous && result.Sink is { } current && Math.Abs(previous - current) > 0.0001)
            return ForgemagingOutcome.Neutral;
        // No visible losses is insufficient without confirmation that the pool did not change.
        return result.PoolStatus == 0 ? ForgemagingOutcome.Critical : ForgemagingOutcome.Unconfirmed;
    }
}
