using System.Globalization;
using System.Text;

namespace BestCrush.Network.Protocol;

/// <summary>
/// Local-only identity search input. Never persist or log the reference,
/// including in diagnostics. The reference is NOT verified evidence.
/// </summary>
public readonly record struct CharacterNameLevelReference(string Name, int Level)
{
    public override string ToString() => "[character reference: redacted]";
}

public static class CharacterNameLevelReferenceInput
{
    public static bool TryParse(string? raw, out CharacterNameLevelReference value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 100)
            return false;

        int delimiter = raw.LastIndexOf(':');
        if (delimiter is < 3 or >= 80 || delimiter == raw.Length - 1)
            return false;

        string name = raw[..delimiter].Trim();
        if (name.Length is < 3 or > 40 ||
            name.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c == ':') ||
            !int.TryParse(raw[(delimiter + 1)..].Trim(), NumberStyles.None,
                CultureInfo.InvariantCulture, out int level) ||
            level is < 1 or > 200)
            return false;

        value = new CharacterNameLevelReference(name, level);
        return true;
    }

    public static void Validate(CharacterNameLevelReference reference)
    {
        if (!TryParse(reference.Name + ":" +
            reference.Level.ToString(CultureInfo.InvariantCulture), out
            CharacterNameLevelReference parsed) || parsed != reference)
            throw new ArgumentException("Invalid local character reference.",
                nameof(reference));
    }
}

/// <summary>
/// Routes the one local identity reference to exactly one diagnostic mode.
/// Kept separate from the CLI so the P2a/P2b exclusivity is testable.
/// </summary>
public readonly record struct CharacterIdentityDiagnosticReferences(
    CharacterNameLevelReference? NameSearch,
    CharacterNameLevelReference? FieldStudy)
{
    public override string ToString() => "[character diagnostic references: redacted]";
}

public static class CharacterIdentityDiagnosticReferenceRouter
{
    public static CharacterIdentityDiagnosticReferences Route(
        bool identityName, bool identityFields,
        CharacterNameLevelReference? reference)
    {
        if (identityName && identityFields)
            throw new ArgumentException("Identity modes must be exclusive.");

        if (identityName || identityFields)
        {
            if (reference is null)
                throw new ArgumentException(
                    "Selected identity mode requires a local reference.",
                    nameof(reference));
            CharacterNameLevelReferenceInput.Validate(reference.Value);
        }
        else if (reference is not null)
        {
            throw new ArgumentException(
                "Identity reference was provided without a selected mode.",
                nameof(reference));
        }

        return new CharacterIdentityDiagnosticReferences(
            identityName ? reference : null,
            identityFields ? reference : null);
    }
}

/// <summary>
/// P2a research-only, value-blind search of exact UTF-8 name fields
/// in bounded protobuf submessages. A level match in the same parent
/// node is only a proximity clue, never evidence of stable character ID.
/// </summary>
public static class CharacterIdentityNameProbe
{
    private const int MaxBodyBytes = 8 * 1024;
    private const int MaxNestedBytes = 8 * 1024;
    private const int MaxDepth = 5;
    private const int MaxFields = 6000;
    private const int MaxOutputPaths = 8;

    public static string Describe(ReadOnlySpan<byte> body,
        CharacterNameLevelReference reference)
    {
        CharacterNameLevelReferenceInput.Validate(reference);
        if (body.IsEmpty || body.Length > MaxBodyBytes)
            return "skipped-size";

        byte[] needle = Encoding.UTF8.GetBytes(reference.Name);
        int budget = MaxFields;
        HashSet<string> paths = new(StringComparer.Ordinal);
        HashSet<string> levelSiblings = new(StringComparer.Ordinal);
        try
        {
            Scan(body, "", 0, needle, (ulong)reference.Level,
                ref budget, paths, levelSiblings);
        }
        catch (OverflowException)
        {
            return "unrecognized";
        }
        catch (ArgumentOutOfRangeException)
        {
            return "unrecognized";
        }

        string matched = Format(paths);
        string levels = Format(levelSiblings);
        return $"name-paths=[{matched}] level-siblings=[{levels}]" +
               " identity=unverified";
    }

    private static string Format(HashSet<string> paths) =>
        paths.Count == 0
            ? "none"
            : string.Join(" ", paths.OrderBy(s => s, StringComparer.Ordinal)
                .Take(MaxOutputPaths)) +
                (paths.Count > MaxOutputPaths ? " ..." : "");

    private static void Scan(ReadOnlySpan<byte> node, string parent,
        int depth, byte[] nameBytes, ulong level,
        ref int budget, HashSet<string> paths,
        HashSet<string> levelSiblings)
    {
        if (depth > MaxDepth || budget <= 0 ||
            node.Length is < 1 or > MaxNestedBytes)
            return;

        List<ProtoField>? fields = ProtoWire.ReadFields(node);
        if (fields is null || fields.Count > budget)
            return;

        budget -= fields.Count;
        ProtoField[] names = fields
            .Where(f => f.WireType == ProtoWireType.LengthDelimited &&
                f.Bytes is not null && f.Bytes.AsSpan().SequenceEqual(nameBytes))
            .ToArray();

        if (names.Length > 0)
        {
            foreach (ProtoField field in names)
                paths.Add(parent + "f" + field.Number);

            // This says only that a nearby varint equals the user's level;
            // many fields may coincide numerically by chance.
            foreach (ProtoField f in fields.Where(f =>
                f.WireType == ProtoWireType.Varint && f.Varint == level))
                levelSiblings.Add(parent + "f" + f.Number);
        }

        if (depth == MaxDepth)
            return;

        foreach (ProtoField child in fields)
        {
            if (budget <= 0)
                break;
            if (child.WireType != ProtoWireType.LengthDelimited ||
                child.Bytes is null ||
                child.Bytes.Length is < 1 or > MaxNestedBytes ||
                child.Bytes.AsSpan().SequenceEqual(nameBytes))
                continue;

            Scan(child.Bytes, parent + "f" + child.Number + ".",
                depth + 1, nameBytes, level,
                ref budget, paths, levelSiblings);
        }
    }
}
