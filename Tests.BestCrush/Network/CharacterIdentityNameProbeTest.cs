using System.Text;
using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>All names, levels and messages are synthetic; no player data.</summary>
public sealed class CharacterIdentityNameProbeTest
{
    private static readonly CharacterNameLevelReference Reference =
        new("Essai-Alpha", 137);

    private static byte[] SyntheticMessage() => Message(
        Unsigned(1, 20991),
        Bytes(2, Message(
            Unsigned(1, 137),
            Bytes(3, Encoding.UTF8.GetBytes("Essai-Alpha")),
            Unsigned(4, 8))),
        Bytes(5, Encoding.UTF8.GetBytes("Unrelated")));

    [Fact]
    public void FindsExactNamePathAndSiblingLevelWithoutDisclosingValues()
    {
        string summary = CharacterIdentityNameProbe.Describe(
            SyntheticMessage(), Reference);
        summary.Should().Contain("name-paths=[f2.f3]");
        summary.Should().Contain("level-siblings=[f2.f1]");
        summary.Should().Contain("identity=unverified");
        summary.Should().NotContain("Essai-Alpha");
        summary.Should().NotContain("137");
        summary.Should().NotContain("20991");
    }

    [Fact]
    public void AcceptsOnlyProperLocalNameLevelInputs()
    {
        CharacterNameLevelReferenceInput.TryParse("Essai-Alpha:137", out var parsed)
            .Should().BeTrue();
        parsed.Should().Be(Reference);
        parsed.ToString().Should().Be("[character reference: redacted]");

        foreach (string bad in new[]
        {
            "X:3", "Essai-Alpha:0", "Essai-Alpha:201",
            "Essai-Alpha:abc", "Bad Name:45", "Bad\nName:45",
            "Nom:100:extra"
        })
            CharacterNameLevelReferenceInput.TryParse(bad, out _)
                .Should().BeFalse();
    }

    [Fact]
    public void EmbeddedSubstringAndDifferentCaseCannotBeMistakenForExactName()
    {
        byte[] wrong = Message(
            Bytes(1, Encoding.UTF8.GetBytes("Essai-Alpha-Plus")),
            Bytes(2, Encoding.UTF8.GetBytes("essai-alpha")),
            Unsigned(3, 137));

        string summary = CharacterIdentityNameProbe.Describe(wrong, Reference);
        summary.Should().Contain("name-paths=[none]");
        summary.Should().Contain("level-siblings=[none]");
    }

    [Fact]
    public void RejectsMalformedOrOversizedWireAnonymously()
    {
        CharacterIdentityNameProbe.Describe([0], Reference)
            .Should().Contain("name-paths=[none]");
        CharacterIdentityNameProbe.Describe(new byte[8193], Reference)
            .Should().Be("skipped-size");
        CharacterIdentityNameProbe.Describe([], Reference)
            .Should().Be("skipped-size");
    }

    [Fact]
    public void InvalidDirectReferenceFailsBeforeWireInspection()
    {
        Action action = () => CharacterIdentityNameProbe.Describe(
            SyntheticMessage(), new("Injected\nName", 137));
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackerOnlyReportsConfiguredOpcodeForEitherDirection()
    {
        var tracker = new CharacterCaptureFlowTracker(
            characterNameReference: Reference,
            characterNameOpCodes: new HashSet<string> { "jev" });
        CharacterCaptureFlowKey key = new(
            "127.0.0.1", 40001, "10.0.0.1", 5555);
        DateTime now = new(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc);
        byte[] msg = Frame(Any("jev", SyntheticMessage()));
        byte[] other = Frame(Any("jgi", SyntheticMessage()));

        var found = tracker.Observe(key,
            CharacterCaptureDirection.ServerToClient,
            100, msg, now)
            .Single(x => x.Kind == CharacterCaptureEventKind.Message);
        found.CharacterNameProbeSummary.Should().Contain("f2.f3");
        found.CharacterNameProbeSummary.Should().NotContain(Reference.Name);
        found.ProfessionIdentifySummary.Should().BeNull();

        var ignored = tracker.Observe(key,
            CharacterCaptureDirection.ClientToServer,
            100, other, now)
            .Single(x => x.Kind == CharacterCaptureEventKind.Message);
        ignored.CharacterNameProbeSummary.Should().BeNull();
    }

    [Fact]
    public void DifferentTcpSessionsRemainDistinctAndUnattributed()
    {
        var tracker = new CharacterCaptureFlowTracker(
            characterNameReference: Reference,
            characterNameOpCodes: new HashSet<string> { "jev" });
        var now = new DateTime(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc);
        byte[] msg = Frame(Any("jev", SyntheticMessage()));
        var first = tracker.Observe(
            new("127.0.0.1", 40001, "10.0.0.1", 5555),
            CharacterCaptureDirection.ServerToClient, 100, msg, now)
            .Single(x => x.Kind == CharacterCaptureEventKind.Message);
        var second = tracker.Observe(
            new("127.0.0.1", 40002, "10.0.0.1", 5555),
            CharacterCaptureDirection.ServerToClient, 100, msg, now)
            .Single(x => x.Kind == CharacterCaptureEventKind.Message);
        first.SessionNumber.Should().NotBe(second.SessionNumber);
        first.CharacterNameProbeSummary.Should().Be(second.CharacterNameProbeSummary);
    }

    [Fact]
    public void RejectsIdentityModeWithOtherModes()
    {
        Action action = () => new CharacterCaptureFlowTracker(
            characterNameReference: Reference,
            shapeKeys: new HashSet<string> { "jev" },
            characterNameOpCodes: new HashSet<string> { "jev" });
        action.Should().Throw<ArgumentException>();
    }
}
