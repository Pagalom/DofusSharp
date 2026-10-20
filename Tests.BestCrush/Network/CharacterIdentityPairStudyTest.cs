using System.Text;
using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class CharacterIdentityPairStudyTest
{
    private static readonly CharacterNameLevelReference A = new("Explorer-A", 179);
    private static readonly CharacterNameLevelReference B = new("Explorer-B", 120);
    private static readonly DateTime Time = new(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);

    private static byte[] Leaf(CharacterNameLevelReference who) =>
        Message(Unsigned(3, (ulong)who.Level),
            Bytes(6, Encoding.UTF8.GetBytes(who.Name)));

    private static byte[] Ksb(CharacterNameLevelReference who, ulong f3) =>
        Bytes(1, Bytes(1, Message(
            Unsigned(3, f3), Bytes(4, Leaf(who)))));

    private static byte[] Ksc(CharacterNameLevelReference who, ulong f3) =>
        Bytes(2, Message(Unsigned(3, f3), Bytes(4, Leaf(who))));

    [Fact]
    public void TwoDifferentTcpSessionsWithDifferentF3ReturnDistinctOnlyAfterTwoProofs()
    {
        CharacterIdentityPairStudy study = new(A, B);
        study.Observe(2, "ksc", Ksc(A, 123456))
            .Should().Contain("confirmed=0/2");
        study.Observe(2, "ksb", Ksb(A, 123456))
            .Should().Contain("confirmed=1/2");
        study.Observe(4, "ksc", Ksc(B, 987654))
            .Should().Contain("confirmed=1/2");

        string result = study.Observe(4, "ksb", Ksb(B, 987654));
        result.Should().Be("p3m3=pair-complete f3=distinct semantic=unverified");
        result.Should().NotContain(A.Name).And.NotContain(B.Name);
        result.Should().NotContain("123456").And.NotContain("987654");
        study.ToString().Should().NotContain(A.Name).And.NotContain(B.Name);
    }

    [Fact]
    public void SameF3ForTwoDifferentReferencesIsReportedButNotMisrepresentedAsIdentity()
    {
        CharacterIdentityPairStudy study = new(A, B);
        study.Observe(2, "ksc", Ksc(A, 321));
        study.Observe(2, "ksb", Ksb(A, 321));
        study.Observe(4, "ksc", Ksc(B, 321));

        study.Observe(4, "ksb", Ksb(B, 321))
            .Should().Be("p3m3=pair-complete f3=same semantic=unverified");
    }

    [Fact]
    public void KscRosterWithBothCharactersDoesNotVerifySecondCharactersSelection()
    {
        CharacterIdentityPairStudy study = new(A, B);
        byte[] roster = Message(
            Ksc(A, 111), Ksc(B, 222));
        study.Observe(2, "ksc", roster).Should().Contain("confirmed=0/2");
        study.Observe(2, "ksb", Ksb(A, 111))
            .Should().Contain("confirmed=1/2");
        study.Observe(4, "ksb", Ksb(B, 222))
            .Should().Contain("confirmed=1/2",
                "a ksc in an earlier different TCP session cannot confirm the new session");
        study.Observe(4, "ksc", roster)
            .Should().Be("p3m3=pair-complete f3=distinct semantic=unverified");
    }

    [Fact]
    public void MismatchedKscKsbF3IsConflicted()
    {
        CharacterIdentityPairStudy study = new(A, B);
        study.Observe(2, "ksc", Ksc(A, 111));
        study.Observe(2, "ksb", Ksb(A, 222))
            .Should().Be("p3m3=conflicted comparison=unverified");
    }

    [Fact]
    public void SameReferenceChangingF3AcrossSessionsFailsClosed()
    {
        CharacterIdentityPairStudy study = new(A, B);
        study.Observe(2, "ksc", Ksc(A, 111));
        study.Observe(2, "ksb", Ksb(A, 111));
        study.Observe(4, "ksc", Ksc(A, 222));
        study.Observe(4, "ksb", Ksb(A, 222))
            .Should().Be("p3m3=conflicted comparison=unverified");
    }

    [Fact]
    public void BothCharactersConfirmedOnSameTcpSessionCannotCompletePair()
    {
        CharacterIdentityPairStudy study = new(A, B);
        byte[] roster = Message(Ksc(A, 111), Ksc(B, 222));
        study.Observe(2, "ksc", roster);
        study.Observe(2, "ksb", Ksb(A, 111));
        study.Observe(2, "ksb", Ksb(B, 222))
            .Should().Be("p3m3=same-tcp-session comparison=unverified");
    }

    [Fact]
    public void OneFamilyAloneNeverCompletesPair()
    {
        CharacterIdentityPairStudy study = new(A, B);
        study.Observe(2, "ksb", Ksb(A, 111));
        study.Observe(4, "ksb", Ksb(B, 222))
            .Should().Contain("confirmed=0/2");
    }

    [Fact]
    public void RejectDuplicateNamesRegardlessOfDifferentLevels()
    {
        Action action = () => new CharacterIdentityPairStudy(
            new CharacterNameLevelReference("SameName", 99),
            new CharacterNameLevelReference("samename", 120));
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ActiveTrackerUsesOpaquePairModeOnDistinctNetworkFlows()
    {
        CharacterIdentityPairStudy study = new(A, B);
        CharacterCaptureFlowTracker tracker = new(identityPairStudy: study);
        CharacterCaptureFlowKey flowA = new("192.0.2.10", 48001, "198.51.100.20", 5555);
        CharacterCaptureFlowKey flowB = new("192.0.2.10", 48002, "198.51.100.20", 5555);

        static string Summary(IReadOnlyList<CharacterCaptureEvent> events) =>
            events.Single(e => e.Kind == CharacterCaptureEventKind.Message)
                .CharacterIdentityFieldSummary!;

        byte[] kscA = Frame(Any("ksc", Ksc(A, 100)));
        byte[] ksbA = Frame(Any("ksb", Ksb(A, 100)));
        byte[] kscB = Frame(Any("ksc", Ksc(B, 200)));
        byte[] ksbB = Frame(Any("ksb", Ksb(B, 200)));
        tracker.Observe(flowA, CharacterCaptureDirection.ServerToClient, 100,
            kscA, Time);
        Summary(tracker.Observe(flowA, CharacterCaptureDirection.ServerToClient,
            (uint)(100 + kscA.Length), ksbA, Time.AddSeconds(1)))
            .Should().Contain("confirmed=1/2");

        tracker.Observe(flowB, CharacterCaptureDirection.ServerToClient,
            100, kscB, Time.AddSeconds(2));
        Summary(tracker.Observe(flowB, CharacterCaptureDirection.ServerToClient,
            (uint)(100 + kscB.Length), ksbB, Time.AddSeconds(3)))
            .Should().Be("p3m3=pair-complete f3=distinct semantic=unverified");
    }
}
