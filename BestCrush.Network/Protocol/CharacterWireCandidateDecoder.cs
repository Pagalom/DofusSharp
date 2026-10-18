using System.Text;

namespace BestCrush.Network.Protocol;

/// <summary>
/// Experimental P2f candidate for Dofus build 3.6.11.15 only.
/// CharacterIdCandidate and Name are sensitive, transient values.
/// Never log or persist this object and never treat it as confirmed identity.
/// </summary>
public sealed class CharacterWireCandidate
{
    public ulong CharacterIdCandidate { get; }
    public string Name { get; }
    public int Level { get; }
    /// <summary>P2j exploratory f5.f3 value, not an authenticated class.</summary>
    public int? ClassIdCandidate { get; }
    public int? ClassId => null;

    internal CharacterWireCandidate(
        ulong id, string name, int level, int? classIdCandidate)
    {
        CharacterIdCandidate = id;
        Name = name;
        Level = level;
        ClassIdCandidate = classIdCandidate;
    }

    public override string ToString() => "[character candidate: redacted]";
}

/// <summary>
/// Research-only decoded candidate snapshot, unrelated to active game
/// character selection, TCP identity validation or character persistence.
/// </summary>
public sealed class CharacterWireCandidateSnapshot
{
    public string Opcode { get; }
    public IReadOnlyList<CharacterWireCandidate> Candidates { get; }
    public bool IdentityConfirmed => false;

    internal CharacterWireCandidateSnapshot(
        string opcode, IReadOnlyList<CharacterWireCandidate> candidates)
    {
        Opcode = opcode;
        Candidates = Array.AsReadOnly(candidates.ToArray());
    }

    public override string ToString() => "[character candidate snapshot: redacted]";
}

/// <summary>
/// Strict, bounded P2f decoder for the observed ksb and ksc wire paths.
/// The real meaning of these two message families and their independence
/// still requires validation. This class is intentionally NOT wired to
/// CharacterIdentityCorrelator, CurrentCharacterState, production routing,
/// database storage or automatic ipz-to-character association.
/// </summary>
public static class CharacterWireCandidateDecoder
{
    public const string StudiedClientBuild = "3.6.11.15";
    private const int MaxBodyBytes = 8192;
    private const int MaxNodeFields = 256;
    private const int MaxSelectableRows = 16;
    private const int MaxClassNodeBytes = 2048;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static bool TryDecode(
        string opcode, ReadOnlySpan<byte> body,
        out CharacterWireCandidateSnapshot? snapshot)
    {
        snapshot = null;
        if (opcode is not ("ksb" or "ksc") ||
            body.IsEmpty || body.Length > MaxBodyBytes)
            return false;

        try
        {
            List<ProtoField>? root = ReadNode(body.ToArray());
            if (root is null)
                return false;

            if (opcode == "ksb")
            {
                if (!TryGetSingleBytes(root, 1, out byte[]? outer) ||
                    outer is null)
                    return false;

                List<ProtoField>? container = ReadNode(outer);
                if (container is null ||
                    !TryGetSingleBytes(container, 1, out byte[]? row) ||
                    row is null ||
                    !TryDecodeRow(row, out CharacterWireCandidate? candidate) ||
                    candidate is null)
                    return false;

                snapshot = new CharacterWireCandidateSnapshot(
                    opcode, [candidate]);
                return true;
            }

            // ksc is a LIST of candidates, not proof of the active character.
            ProtoField[] list = root.Where(f => f.Number == 2).ToArray();
            if (list.Length == 0 || list.Length > MaxSelectableRows ||
                list.Length != root.Count)
                return false;

            List<CharacterWireCandidate> candidates = [];
            HashSet<ulong> ids = [];
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (ProtoField field in list)
            {
                if (field.WireType != ProtoWireType.LengthDelimited ||
                    field.Bytes is null ||
                    !TryDecodeRow(field.Bytes, out CharacterWireCandidate? person) ||
                    person is null ||
                    !ids.Add(person.CharacterIdCandidate) ||
                    !names.Add(person.Name))
                    return false;

                candidates.Add(person);
            }

            snapshot = new CharacterWireCandidateSnapshot(opcode, candidates);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>
    /// Checks whether a ksb candidate is present exactly once in a ksc list.
    /// This is wire-consistency evidence only, NOT independent confirmation.
    /// Callers must separately enforce the SAME TCP connection and lifecycle.
    /// </summary>
    public static bool HasMatchingCandidate(
        CharacterWireCandidateSnapshot ksb,
        CharacterWireCandidateSnapshot ksc)
    {
        ArgumentNullException.ThrowIfNull(ksb);
        ArgumentNullException.ThrowIfNull(ksc);
        if (ksb.Opcode != "ksb" || ksc.Opcode != "ksc" ||
            ksb.Candidates.Count != 1)
            return false;

        CharacterWireCandidate active = ksb.Candidates[0];
        return ksc.Candidates.Count(person =>
            person.CharacterIdCandidate == active.CharacterIdCandidate &&
            string.Equals(person.Name, active.Name, StringComparison.Ordinal) &&
            person.Level == active.Level) == 1;
    }

    /// <summary>
    /// Structural equality of two class candidates on one previously matched
    /// character entry. Null means unverified/absent, never a class ID.
    /// TCP/session equivalence is enforced only by the calling tracker.
    /// </summary>
    public static bool? CompareClassCandidates(
        CharacterWireCandidateSnapshot ksb,
        CharacterWireCandidateSnapshot ksc)
    {
        if (!HasMatchingCandidate(ksb, ksc))
            return null;

        CharacterWireCandidate loaded = ksb.Candidates[0];
        CharacterWireCandidate? listed = ksc.Candidates.SingleOrDefault(c =>
            c.CharacterIdCandidate == loaded.CharacterIdCandidate &&
            string.Equals(c.Name, loaded.Name, StringComparison.Ordinal) &&
            c.Level == loaded.Level);

        if (listed?.ClassIdCandidate is null ||
            loaded.ClassIdCandidate is null)
            return null;

        return listed.ClassIdCandidate.Value == loaded.ClassIdCandidate.Value;
    }

    private static bool TryDecodeRow(
        byte[] row, out CharacterWireCandidate? candidate)
    {
        candidate = null;
        List<ProtoField>? parent = ReadNode(row);
        if (parent is null ||
            !TryGetSingleVarint(parent, 3, out ulong characterId) ||
            characterId == 0 ||
            !TryGetSingleBytes(parent, 4, out byte[]? leafBytes) ||
            leafBytes is null)
            return false;

        List<ProtoField>? leaf = ReadNode(leafBytes);
        if (leaf is null ||
            !TryGetSingleVarint(leaf, 3, out ulong level) ||
            level is < 1 or > 200 ||
            !TryGetSingleBytes(leaf, 6, out byte[]? nameBytes) ||
            nameBytes is null || nameBytes.Length is < 3 or > 80)
            return false;

        string name = StrictUtf8.GetString(nameBytes);
        if (name.Length is < 3 or > 40 ||
            name.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
            return false;

        // P2j: a separate, optional, unverified class hypothesis.
        // Invalid, absent or duplicated f5/f5.f3 never supplies a class.
        // Core ID/name/level observations remain research-only.
        int? classIdCandidate = ReadClassCandidate(leaf);
        candidate = new CharacterWireCandidate(
            characterId, name, (int)level, classIdCandidate);
        return true;
    }

    private static int? ReadClassCandidate(List<ProtoField> leaf)
    {
        ProtoField[] fields = leaf.Where(f => f.Number == 5).ToArray();
        if (fields.Length != 1 ||
            fields[0].WireType != ProtoWireType.LengthDelimited ||
            fields[0].Bytes is not { Length: > 0 and <= MaxClassNodeBytes })
            return null;

        List<ProtoField>? nested = ReadNode(fields[0].Bytes);
        if (nested is null ||
            !TryGetSingleVarint(nested, 3, out ulong classCandidate) ||
            classCandidate is < 1 or > 19)
            return null;

        return (int)classCandidate;
    }

    private static List<ProtoField>? ReadNode(byte[] bytes)
    {
        if (bytes.Length is < 1 or > MaxBodyBytes)
            return null;
        List<ProtoField>? fields = ProtoWire.ReadFields(bytes);
        return fields is { Count: > 0 and <= MaxNodeFields }
            ? fields : null;
    }

    private static bool TryGetSingleVarint(
        List<ProtoField> node, int number, out ulong value)
    {
        value = 0;
        ProtoField[] matches = node.Where(f => f.Number == number).ToArray();
        if (matches.Length != 1 ||
            matches[0].WireType != ProtoWireType.Varint)
            return false;

        value = matches[0].Varint;
        return true;
    }

    private static bool TryGetSingleBytes(
        List<ProtoField> node, int number, out byte[]? bytes)
    {
        bytes = null;
        ProtoField[] matches = node.Where(f => f.Number == number).ToArray();
        if (matches.Length != 1 ||
            matches[0].WireType != ProtoWireType.LengthDelimited ||
            matches[0].Bytes is not { Length: > 0 and <= MaxBodyBytes })
            return false;

        bytes = matches[0].Bytes;
        return true;
    }
}
