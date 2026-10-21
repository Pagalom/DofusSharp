using System.Text;
using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>Synthetic data only. Neither account details nor live packets.</summary>
public sealed class CharacterCrossOpcodeFieldStudyTest
{
    private static readonly CharacterNameLevelReference Reference =
        new("Research-Hero", 179);
    private static readonly DateTime T0 =
        new(2026, 10, 21, 20, 0, 0, DateTimeKind.Utc);

    private static byte[] Leaf() => Message(
        Unsigned(3, (ulong)Reference.Level),
        Bytes(6, Encoding.UTF8.GetBytes(Reference.Name)));
    private static byte[] Ksb(ulong f3) =>
        Bytes(1, Bytes(1, Message(Unsigned(3, f3), Bytes(4, Leaf()))));
    private static byte[] Ksc(ulong f3) =>
        Bytes(2, Message(Unsigned(3, f3), Bytes(4, Leaf())));
    private static byte[] Other(ulong value) =>
        Message(Unsigned(2, 9), Bytes(5, Message(Unsigned(3, value))));

    [Fact]
    public void BuffersOnlyNumericCandidatesSeenBeforeKscAndKsb()
    {
        CharacterCrossOpcodeFieldStudy study = new(Reference);
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "krg", Other(456789)).Should().BeNull();
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "ksc", Ksc(456789)).Should().Contain("linked=pending");
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "ksb", Ksb(456789)).Should().Contain("linked=yes");

        string output = string.Join("\n", study.Summarize());
        output.Should().Contain("candidate-paths=[krg:f5.f3]")
            .And.Contain("meaning=unverified")
            .And.Contain("server=unverified");
        output.Should().NotContain(Reference.Name)
            .And.NotContain("456789");
        study.ToString().Should().NotContain(Reference.Name);
    }

    [Fact]
    public void DoesNotTreatKscAndKsbAsIndependentThirdFamily()
    {
        CharacterCrossOpcodeFieldStudy study = new(Reference);
        study.Observe(1, CharacterCaptureDirection.ServerToClient,
            "ksc", Ksc(999));
        study.Observe(1, CharacterCaptureDirection.ServerToClient,
            "ksb", Ksb(999));
        string output = string.Join("\n", study.Summarize());
        output.Should().Contain("linked=yes")
            .And.Contain("candidate-paths=[none]")
            .And.Contain("cross-session-f3=unavailable");
    }

    [Fact]
    public void DifferentSessionNumericMatchCannotBeLinkedToCurrentSession()
    {
        CharacterCrossOpcodeFieldStudy study = new(Reference);
        study.Observe(1, CharacterCaptureDirection.ServerToClient,
            "krg", Other(888));
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "ksc", Ksc(888));
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "ksb", Ksb(888));
        string output = string.Join("\n", study.Summarize());
        output.Should().Contain("p3m4=session=1 linked=no")
            .And.Contain("p3m4=session=2 linked=yes observed=0 candidate-paths=[none]");
        output.Should().NotContain("krg:f5.f3");
    }

    [Fact]
    public void ConflictingIdentityValuesFailClosedEvenIfOtherOpcodeMatches()
    {
        CharacterCrossOpcodeFieldStudy study = new(Reference);
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "krg", Other(777));
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "ksc", Ksc(777));
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "ksb", Ksb(778)).Should().Contain("p3m4=conflicted");
        string output = string.Join("\n", study.Summarize());
        output.Should().Contain("linked=no").And.NotContain("candidate-paths=[krg");
    }

    [Fact]
    public void UnrelatedOpcodeAndClientToServerPayloadCannotProduceEvidence()
    {
        CharacterCrossOpcodeFieldStudy study = new(Reference);
        study.Observe(2, CharacterCaptureDirection.ClientToServer,
            "krg", Other(1234)).Should().BeNull();
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "untrusted", Other(1234)).Should().BeNull();
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "ksc", Ksc(1234));
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "ksb", Ksb(1234));
        string output = string.Join("\n", study.Summarize());
        output.Should().Contain("observed=0 candidate-paths=[none]");
    }

    [Fact]
    public void SameCandidateAcrossSessionsIsOnlyAConsistencyObservation()
    {
        CharacterCrossOpcodeFieldStudy study = new(Reference);
        foreach (int session in new[] { 2, 4 })
        {
            study.Observe(session, CharacterCaptureDirection.ServerToClient,
                "krg", Other(333));
            study.Observe(session, CharacterCaptureDirection.ServerToClient,
                "ksc", Ksc(333));
            study.Observe(session, CharacterCaptureDirection.ServerToClient,
                "ksb", Ksb(333));
        }

        string output = string.Join("\n", study.Summarize());
        output.Should().Contain("cross-session-f3=equal")
            .And.Contain("linked-sessions=2")
            .And.Contain("server=unverified");
        output.Split("krg:f5.f3").Length.Should().Be(3);
    }

    [Fact]
    public void ThirdFamilyEqualityMayBeCoincidentalAndNeverConfirmsIdentity()
    {
        CharacterCrossOpcodeFieldStudy study = new(Reference);
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "krg", Other(9));
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "ksc", Ksc(11));
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "ksb", Ksb(11));
        string output = string.Join("\n", study.Summarize());
        output.Should().Contain("candidate-paths=[none]")
            .And.Contain("meaning=unverified");
    }

    [Fact]
    public void TrackerRoutesOtherOpcodeCandidatesWithinSameConnectionOnly()
    {
        CharacterCrossOpcodeFieldStudy study = new(Reference);
        CharacterCaptureFlowTracker tracker = new(crossOpcodeStudy: study);
        CharacterCaptureFlowKey tcp = new(
            "192.0.2.3", 48888, "198.51.100.2", 5555);
        byte[] krg = Frame(Any("krg", Other(7654)));
        byte[] ksc = Frame(Any("ksc", Ksc(7654)));
        byte[] ksb = Frame(Any("ksb", Ksb(7654)));

        tracker.Observe(tcp, CharacterCaptureDirection.ServerToClient,
            100, krg, T0);
        tracker.Observe(tcp, CharacterCaptureDirection.ServerToClient,
            (uint)(100 + krg.Length), ksc, T0.AddSeconds(1));
        var events = tracker.Observe(tcp, CharacterCaptureDirection.ServerToClient,
            (uint)(100 + krg.Length + ksc.Length), ksb, T0.AddSeconds(2));

        events.Single(e => e.Kind == CharacterCaptureEventKind.Message)
            .CharacterIdentityFieldSummary.Should().Contain("linked=yes");
        string output = string.Join("\n", study.Summarize());
        output.Should().Contain("candidate-paths=[krg:f5.f3]");
    }

    [Fact]
    public void CannotMixWithOtherIdentityDiagnostic()
    {
        CharacterCrossOpcodeFieldStudy study = new(Reference);
        Action action = () => new CharacterCaptureFlowTracker(
            identityFieldsReference: Reference, crossOpcodeStudy: study);
        action.Should().Throw<ArgumentException>();
    }
}
