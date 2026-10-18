using System.Text;
using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>Fully synthetic observations, never derived from player traffic.</summary>
public sealed class CharacterIdentityFieldStudyTest
{
    private static readonly CharacterNameLevelReference Local = new("Exemple-Ciel", 137);

    private static byte[] Person(string name, ulong id, int level = 137, int type = 8) =>
        Message(Unsigned(1, id), Unsigned(2, (ulong)type),
            Unsigned(3, (ulong)level),
            Bytes(6, Encoding.UTF8.GetBytes(name)));

    private static byte[] Ksb(ulong id = 9000123, string name = "Exemple-Ciel",
        int level = 137) =>
        Bytes(1, Bytes(1, Message(
            Unsigned(3, id),
            Bytes(4, Person(name, id, level)))));

    private static byte[] Ksc(ulong id = 9000123,
        string name = "Exemple-Ciel", bool duplicate = false) =>
        Message(
            Bytes(2, Message(Unsigned(3, 9000789), Bytes(4, Person("Autre-Ciel", 9000789, 100)))),
            Bytes(2, Message(Unsigned(3, id), Bytes(4, Person(name, id)))),
            duplicate ? Bytes(2, Message(Unsigned(3, id), Bytes(4, Person(name, id)))) : []);

    [Fact]
    public void BothWireFamiliesExposeNumericSiblingsOnlyWhenNameAndLevelMatch()
    {
        var first = CharacterIdentityFieldStudy.Observe("ksb", Ksb(), Local, out var ksb);
        var second = CharacterIdentityFieldStudy.Observe("ksc", Ksc(), Local, out var ksc);

        first.Should().Contain("match=unique numeric-siblings=[f1,f2] parent-numeric=[f3]");
        second.Should().Contain("match=unique numeric-siblings=[f1,f2] parent-numeric=[f3]");
        ksb.Should().NotBeNull();
        ksc.Should().NotBeNull();
        ksb!.ToString().Should().Be("[character identity fields: redacted]");
        var report = CharacterIdentityFieldStudy.Compare(ksb, ksc!);
        report.Should().Contain("shared-equal-numeric=[f1,f2]");
        report.Should().Contain("shared-equal-parent=[f3]");
        report.Should().Contain("id=unverified");
        report.Should().NotContain(Local.Name);
        report.Should().NotContain("137");
        report.Should().NotContain("9000123");
    }

    [Fact]
    public void ChangedNumericValuesNeverCountAsEqual()
    {
        CharacterIdentityFieldStudy.Observe("ksb", Ksb(), Local, out var first);
        CharacterIdentityFieldStudy.Observe("ksc", Ksc(9000555), Local, out var second);
        string comparison = CharacterIdentityFieldStudy.Compare(first!, second!);
        comparison.Should().Contain("shared-equal-numeric=[f2]");
        comparison.Should().Contain("shared-equal-parent=[none]");
        comparison.Should().NotContain("f1,");
    }

    [Fact]
    public void DifferentNameOrLevelCannotProduceObservation()
    {
        CharacterIdentityFieldStudy.Observe("ksb", Ksb(name: "Personnage-X"),
            Local, out var otherName).Should().Be("match=absent");
        otherName.Should().BeNull();
        CharacterIdentityFieldStudy.Observe("ksb",
            Ksb(level: 100), Local, out var otherLevel)
            .Should().Be("match=absent");
        otherLevel.Should().BeNull();

        CharacterIdentityFieldStudy.Observe("ksc",
            Ksc(name: "Personnage-X"), Local, out var other)
            .Should().Be("match=absent");
        other.Should().BeNull();
    }

    [Fact]
    public void DuplicateMatchingCharactersAreAmbiguousAndFailClosed()
    {
        CharacterIdentityFieldStudy.Observe("ksc",
            Ksc(duplicate: true), Local, out var obs)
            .Should().Be("match=ambiguous");
        obs.Should().BeNull();
    }

    [Fact]
    public void UnsupportedInvalidOrOversizedMessageNeverLeadsToId()
    {
        CharacterIdentityFieldStudy.Observe("non", Ksb(), Local, out var bad)
            .Should().Be("unsupported");
        bad.Should().BeNull();
        CharacterIdentityFieldStudy.Observe("ksb", [0], Local, out bad)
            .Should().Be("rejected-layout");
        CharacterIdentityFieldStudy.Observe("ksc", new byte[8193], Local, out bad)
            .Should().Be("skipped-size");
        bad.Should().BeNull();
    }

    [Fact]
    public void TrackerComparesOnlyWithinSameConnectionAfterBothFamiliesArrive()
    {
        var tracker = new CharacterCaptureFlowTracker(identityFieldsReference: Local);
        var now = new DateTime(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);
        var a = new CharacterCaptureFlowKey("127.0.0.1", 40001, "10.0.0.1", 5555);
        var b = new CharacterCaptureFlowKey("127.0.0.1", 40002, "10.0.0.1", 5555);
        byte[] first = Frame(Any("ksc", Ksc()));
        byte[] second = Frame(Any("ksb", Ksb()));

        var a1 = tracker.Observe(a, CharacterCaptureDirection.ServerToClient,
            100, first, now).Single(e => e.Kind == CharacterCaptureEventKind.Message);
        a1.CharacterIdentityFieldSummary.Should().Contain("same-session=pending");
        a1.CharacterIdentityFieldSummary.Should().NotContain("shared-equal-numeric");

        var b1 = tracker.Observe(b, CharacterCaptureDirection.ServerToClient,
            100, second, now).Single(e => e.Kind == CharacterCaptureEventKind.Message);
        b1.CharacterIdentityFieldSummary.Should().Contain("same-session=pending");
        b1.SessionNumber.Should().NotBe(a1.SessionNumber);

        var a2 = tracker.Observe(a, CharacterCaptureDirection.ServerToClient,
            (uint)(100 + first.Length), second, now.AddSeconds(1))
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        a2.CharacterIdentityFieldSummary.Should().Contain("same-session=yes");
        a2.CharacterIdentityFieldSummary.Should().Contain("shared-equal-numeric=[f1,f2]");
        a2.CharacterIdentityFieldSummary.Should().Contain("shared-equal-parent=[f3]");
        a2.CharacterIdentityFieldSummary.Should().NotContain("9000123");
        a2.CharacterNameProbeSummary.Should().BeNull();
    }

    [Fact]
    public void RepeatedContradictoryKsbBlocksFurtherStudyInThatSession()
    {
        var tracker = new CharacterCaptureFlowTracker(identityFieldsReference: Local);
        var now = new DateTime(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);
        var scope = new CharacterCaptureFlowKey("127.0.0.1", 40001, "10.0.0.1", 5555);
        byte[] first = Frame(Any("ksb", Ksb()));
        byte[] changed = Frame(Any("ksb", Ksb(9000555)));

        var firstMessage = tracker.Observe(scope, CharacterCaptureDirection.ServerToClient,
            100, first, now).Single(e => e.Kind == CharacterCaptureEventKind.Message);
        firstMessage.CharacterIdentityFieldSummary.Should().Contain("match=unique");

        var next = tracker.Observe(scope, CharacterCaptureDirection.ServerToClient,
            (uint)(100 + first.Length), changed, now.AddSeconds(1))
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        next.CharacterIdentityFieldSummary.Should().Be("status=conflicted identity=unverified");
    }

    [Fact]
    public void ClientPayloadCannotProduceIdentityFieldStudy()
    {
        var tracker = new CharacterCaptureFlowTracker(identityFieldsReference: Local);
        var now = new DateTime(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);
        var scope = new CharacterCaptureFlowKey("127.0.0.1", 40001, "10.0.0.1", 5555);
        var client = tracker.Observe(scope, CharacterCaptureDirection.ClientToServer,
            100, Frame(Any("ksc", Ksc())), now)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        client.CharacterIdentityFieldSummary.Should().BeNull();
    }


    [Fact]
    public void ParentCanContainCandidateWhenLeafHasOnlyNameAndLevel()
    {
        static byte[] PersonMinimal() => Message(
            Unsigned(3, 137),
            Bytes(6, Encoding.UTF8.GetBytes("Exemple-Ciel")));

        byte[] ksb = Bytes(1, Bytes(1, Message(
            Unsigned(3, 9000123),
            Bytes(4, PersonMinimal()))));
        byte[] ksc = Bytes(2, Message(
            Unsigned(3, 9000123),
            Bytes(4, PersonMinimal())));

        string first = CharacterIdentityFieldStudy.Observe(
            "ksb", ksb, Local, out var a);
        string second = CharacterIdentityFieldStudy.Observe(
            "ksc", ksc, Local, out var b);

        first.Should().Contain("numeric-siblings=[none] parent-numeric=[f3]");
        second.Should().Contain("numeric-siblings=[none] parent-numeric=[f3]");
        string report = CharacterIdentityFieldStudy.Compare(a!, b!);
        report.Should().Contain("shared-equal-numeric=[none]");
        report.Should().Contain("shared-equal-parent=[f3]");
        report.Should().NotContain("9000123");
        report.Should().NotContain(Local.Name);
        report.Should().NotContain("137");
    }

    [Fact]
    public void ParentValueChangesBlockStudyEvenIfLeafValueIsUnchanged()
    {
        static byte[] KsbWithParent(ulong parentId) =>
            Bytes(1, Bytes(1, Message(
                Unsigned(3, parentId),
                Bytes(4, Person("Exemple-Ciel", 9000123)))));

        var tracker = new CharacterCaptureFlowTracker(identityFieldsReference: Local);
        var now = new DateTime(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);
        var scope = new CharacterCaptureFlowKey("127.0.0.1", 40003, "10.0.0.1", 5555);
        byte[] a = Frame(Any("ksb", KsbWithParent(9000123)));
        byte[] b = Frame(Any("ksb", KsbWithParent(9000777)));
        tracker.Observe(scope, CharacterCaptureDirection.ServerToClient,
            100, a, now).Single(x => x.Kind == CharacterCaptureEventKind.Message)
            .CharacterIdentityFieldSummary.Should().Contain("parent-numeric=[f3]");
        tracker.Observe(scope, CharacterCaptureDirection.ServerToClient,
            (uint)(100 + a.Length), b, now.AddSeconds(1))
            .Single(x => x.Kind == CharacterCaptureEventKind.Message)
            .CharacterIdentityFieldSummary.Should().Be("status=conflicted identity=unverified");
    }

    [Fact]
    public void P2bCannotRunAlongsideP2aOrOtherModes()
    {
        Action a = () => new CharacterCaptureFlowTracker(
            identityFieldsReference: Local, characterNameReference: Local);
        a.Should().Throw<ArgumentException>();

        Action b = () => new CharacterCaptureFlowTracker(
            identityFieldsReference: Local, shapeKeys: new HashSet<string> { "ksb" });
        b.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void P2bCliReferenceRoutingDoesNotAlsoActivateP2a()
    {
        CharacterIdentityDiagnosticReferences selected =
            CharacterIdentityDiagnosticReferenceRouter.Route(
                identityName: false, identityFields: true, reference: Local);

        selected.NameSearch.Should().BeNull();
        selected.FieldStudy.Should().Be(Local);
        selected.ToString().Should().NotContain(Local.Name);

        // Regression: this exact combination previously threw
        // "P2b field study is an exclusive diagnostic mode."
        Action construct = () => new CharacterCaptureFlowTracker(
            characterNameReference: selected.NameSearch,
            characterNameOpCodes: null,
            identityFieldsReference: selected.FieldStudy);
        construct.Should().NotThrow();
    }

    [Fact]
    public void IdentityRoutingKeepsP2aAvailableAndRejectsBothModes()
    {
        CharacterIdentityDiagnosticReferences nameOnly =
            CharacterIdentityDiagnosticReferenceRouter.Route(
                identityName: true, identityFields: false, reference: Local);
        nameOnly.NameSearch.Should().Be(Local);
        nameOnly.FieldStudy.Should().BeNull();

        Action constructName = () => new CharacterCaptureFlowTracker(
            characterNameReference: nameOnly.NameSearch,
            characterNameOpCodes: new HashSet<string> { "ksb" },
            identityFieldsReference: nameOnly.FieldStudy);
        constructName.Should().NotThrow();

        CharacterIdentityDiagnosticReferences inactive =
            CharacterIdentityDiagnosticReferenceRouter.Route(
                identityName: false, identityFields: false, reference: null);
        inactive.NameSearch.Should().BeNull();
        inactive.FieldStudy.Should().BeNull();

        Action both = () => CharacterIdentityDiagnosticReferenceRouter.Route(
            identityName: true, identityFields: true, reference: Local);
        both.Should().Throw<ArgumentException>();

        Action missingReference = () => CharacterIdentityDiagnosticReferenceRouter.Route(
            identityName: false, identityFields: true, reference: null);
        missingReference.Should().Throw<ArgumentException>();

        Action inactiveWithReference = () => CharacterIdentityDiagnosticReferenceRouter.Route(
            identityName: false, identityFields: false, reference: Local);
        inactiveWithReference.Should().Throw<ArgumentException>();
    }
}
