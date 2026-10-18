namespace BestCrush.Network.Protocol;

/// <summary>
/// P3a research-only flow characterization. One instance per TCP connection.
/// Values and candidate names never appear in emitted summaries, are never
/// stored on disk, and are NOT verified account/server identity or profession ownership.
/// </summary>
public sealed class ProfessionFlowStudy
{
    private CharacterWireCandidateSnapshot? _selectable;
    private CharacterWireCandidateSnapshot? _loaded;

    public string Observe(string opcode, ReadOnlySpan<byte> body)
    {
        switch (opcode)
        {
            case "ksc":
            {
                // A new selection invalidates the previously loaded character observation.
                // Even a malformed new list must not reuse stale state.
                _loaded = null;
                _selectable = null;
                if (!CharacterWireCandidateDecoder.TryDecode(
                    opcode, body, out CharacterWireCandidateSnapshot? list) ||
                    list is null)
                    return "p3a=ksc decoded=no selection-link=pending identity=unverified";

                _selectable = list;
                return "p3a=ksc decoded=yes rows=" + list.Candidates.Count +
                       " selection-link=pending identity=unverified";
            }
            case "ksb":
            {
                // Any subsequent ksb supersedes a previous loaded candidate,
                // including a malformed or different-character message.
                _loaded = null;
                if (!CharacterWireCandidateDecoder.TryDecode(
                    opcode, body, out CharacterWireCandidateSnapshot? candidate) ||
                    candidate is null)
                    return "p3a=ksb decoded=no selection-link=unavailable identity=unverified";

                _loaded = candidate;
                return "p3a=ksb decoded=yes " + DescribeLink() +
                       " identity=unverified";
            }
            case "ipz":
            {
                if (!ProfessionIpzDecoder.TryDecode(body,
                    out ProfessionIpzSnapshot? snapshot) ||
                    snapshot is null)
                    return "p3a=ipz decoded=no " + DescribeLink() +
                           " scope=account-server account=unverified server=unverified mapping=unverified";

                ulong[] f5 = snapshot.Rows
                    .Select(row => row.UnidentifiedVarints.TryGetValue(
                        5, out ulong candidate) ? candidate : 0)
                    .ToArray();
                bool globallyDistinctCandidateF5 =
                    f5.All(id => id > 0) &&
                    f5.Distinct().Count() == snapshot.Rows.Count;
                return "p3a=ipz decoded=yes rows=" + snapshot.Rows.Count +
                       " f5-unique=" +
                       (globallyDistinctCandidateF5 ? "yes" : "no") +
                       " " + DescribeLink() +
                       " scope=account-server account=unverified server=unverified mapping=unverified";
            }
            default:
                return "p3a=unsupported scope=account-server account=unverified server=unverified";
        }
    }

    private string DescribeLink()
    {
        if (_loaded is null)
            return "selection-link=pending class-link=unavailable";

        if (_selectable is null)
            return "selection-link=unavailable class-link=unavailable";

        if (!CharacterWireCandidateDecoder.HasMatchingCandidate(
                _loaded, _selectable))
            return "selection-link=no class-link=unavailable";

        bool? sameClass = CharacterWireCandidateDecoder.CompareClassCandidates(
            _loaded, _selectable);
        return "selection-link=yes class-link=" +
               (sameClass is null ? "unavailable" :
                   sameClass.Value ? "yes" : "no");
    }
}
