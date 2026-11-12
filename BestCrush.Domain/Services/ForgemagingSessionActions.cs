using BestCrush.Domain.Models;

namespace BestCrush.Domain.Services;

public static class ForgemagingSessionActions
{
    public static ForgemagingAttempt Record(ForgemagingSession session, ForgemagingRune rune,
        ForgemagingOutcome outcome, IReadOnlyList<ForgemagingLine> actual, DateTime atUtc)
    {
        if (outcome == ForgemagingOutcome.Unconfirmed) throw new ArgumentException("Choisissez le résultat obtenu dans Dofus.");
        if (ForgemagingPlanner.Validate(session) is { } error) throw new ArgumentException(error);
        if (ForgemagingPlanner.Evaluate(session.Level, session.Lines, rune).Blocked is { } blocked) throw new ArgumentException(blocked);
        var before = session.Lines.Select(l => l.Copy()).ToList();
        var after = before.Select(l => l.Copy()).ToList();
        if (outcome == ForgemagingOutcome.Critical)
            after.Single(l => l.Characteristic == rune.Characteristic).Current += rune.Gain;
        else
        {
            if (actual.Count != before.Count || actual.Select(l => l.Characteristic).Distinct().Count() != actual.Count)
                throw new ArgumentException("Le résultat ne décrit pas toutes les caractéristiques.");
            foreach (var line in after)
            {
                var observed = actual.SingleOrDefault(l => l.Characteristic == line.Characteristic);
                if (observed?.Current is not { } value || Math.Abs((long)value) > 100000)
                    throw new ArgumentException("Renseignez le jet obtenu après la tentative.");
                int expected = line.Current!.Value + (outcome == ForgemagingOutcome.Neutral && line.Characteristic == rune.Characteristic ? rune.Gain : 0);
                if (value > expected || (outcome == ForgemagingOutcome.Neutral && line.Characteristic == rune.Characteristic && value != expected))
                    throw new ArgumentException("Ce jet obtenu ne correspond pas au résultat choisi.");
                line.Current = value;
            }
        }
        double? sink = ForgemagingPlanner.ResultingSink(session.Sink, before, after, rune, outcome);
        var attempt = new ForgemagingAttempt { AtUtc = atUtc, Rune = rune, Outcome = outcome,
            SinkBefore = session.Sink, SinkAfter = sink, Before = before, After = after.Select(l => l.Copy()).ToList() };
        session.Lines = after; session.Sink = sink; session.Attempts.Add(attempt);
        return attempt;
    }

    public static void UndoLast(ForgemagingSession session)
    {
        if (session.Attempts.LastOrDefault() is not { } last) return;
        if (last.FromGame) throw new ArgumentException("Un résultat reçu du jeu ne peut pas être annulé comme une saisie.");
        var targets = session.Lines.ToDictionary(l => l.Characteristic, l => l.Target);
        session.Lines = last.Before.Select(l => l.Copy()).ToList();
        foreach (var line in session.Lines) if (targets.TryGetValue(line.Characteristic, out int target)) line.Target = target;
        session.Sink = last.SinkBefore; session.Attempts.RemoveAt(session.Attempts.Count - 1);
    }

    /// <summary>Observed game states are authoritative, even when outside the planner's recommendations.</summary>
    public static bool RecordObserved(ForgemagingSession session, ForgemagingRune rune,
        ForgemagingOutcome outcome, IReadOnlyDictionary<Characteristic, int> beforeValues,
        IReadOnlyDictionary<Characteristic, int> afterValues, string observationId, DateTime atUtc,
        double? observedSink, int? resultCode = null, int runeCount = 1)
    {
        if (string.IsNullOrWhiteSpace(session.GameRecordingId) || string.IsNullOrWhiteSpace(observationId) || runeCount is < 1 or > 1000)
            throw new ArgumentException("Observation FM invalide.");
        if (session.Attempts.Any(a => a.ObservationId == observationId)) return false;
        if (beforeValues.Values.Concat(afterValues.Values).Any(v => Math.Abs((long)v) > 100000) ||
            observedSink is { } sink && (!double.IsFinite(sink) || sink < 0)) throw new ArgumentException("Jet observé invalide.");
        var stats = session.Lines.Select(l => l.Characteristic).Union(beforeValues.Keys).Union(afterValues.Keys).Distinct().ToArray();
        List<ForgemagingLine> Snapshot(IReadOnlyDictionary<Characteristic, int> values) => stats.Select(stat =>
        {
            var line = session.Lines.FirstOrDefault(l => l.Characteristic == stat)?.Copy() ?? new ForgemagingLine { Characteristic = stat };
            line.Current = values.GetValueOrDefault(stat); return line;
        }).ToList();
        var before = Snapshot(beforeValues); var after = Snapshot(afterValues);
        var odds = ForgemagingPlanner.Evaluate(session.Level, before, rune);
        session.Attempts.Add(new() { AtUtc = atUtc, Rune = rune, Outcome = outcome, FromGame = true,
            ObservationId = observationId, RuneCount = runeCount, GameResultCode = resultCode,
            PredictedOdds = odds.Blocked is null ? odds : null, Before = before, After = after,
            SinkBefore = session.Sink, SinkAfter = observedSink });
        session.Lines = after; session.Sink = observedSink;
        return true;
    }
}
