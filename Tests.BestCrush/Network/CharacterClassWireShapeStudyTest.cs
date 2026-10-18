using System.Text;
using BestCrush.Network.Protocol;
using BestCrush.Network.Capture;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>All protocol payloads and class-like fields are invented synthetic fixtures.</summary>
public sealed class CharacterClassWireShapeStudyTest
{
    private static readonly CharacterNameLevelReference Ref =
        new("Sable-Ciel", 137);

    private static byte[] Leaf(string name, int level, ulong? illustrativeType = null) =>
        Message(
            illustrativeType.HasValue ? Unsigned(1, illustrativeType.Value) : [],
            Unsigned(3, (ulong)level),
            Bytes(6, Encoding.UTF8.GetBytes(name)));

    private static byte[] Row(ulong id, string name, int level,
        ulong? illustrativeType = null, ulong? illustrativeFlag = null) =>
        Message(
            Unsigned(3, id),
            Bytes(4, Leaf(name, level, illustrativeType)),
            illustrativeFlag.HasValue ? Unsigned(5, illustrativeFlag.Value) : []);

    private static byte[] Ksb() =>
        Bytes(1, Bytes(1, Row(9000123, "Sable-Ciel", 137)));

    private static byte[] Ksc(bool withDifferentUnknownFields = false) =>
        Message(
            Bytes(2, Row(9000789, "Roche-Lune", 2,
                illustrativeType: withDifferentUnknownFields ? 20UL : null,
                illustrativeFlag: withDifferentUnknownFields ? 2UL : null)),
            Bytes(2, Row(9000123, "Sable-Ciel", 137,
                illustrativeType: withDifferentUnknownFields ? 10UL : null,
                illustrativeFlag: withDifferentUnknownFields ? 1UL : null)));

    [Fact]
    public void KsbShapeContainsFieldTypesWithoutValues()
    {
        string summary = CharacterClassWireShapeStudy.Describe("ksb", Ksb(), Ref);
        summary.Should().Contain("p2g=observed");
        summary.Should().Contain("leaf=[f3:v,f6:len]");
        summary.Should().Contain("parent=[f3:v,f4:len]");
        summary.Should().Contain("between-characters-diff=[unavailable]");
        summary.Should().Contain("class=unknown");
        summary.Should().NotContain("Sable-Ciel");
        summary.Should().NotContain("9000123");
        summary.Should().NotContain("137");
    }

    [Fact]
    public void KscIdenticalOptionalFieldLayoutsDoNotGuessClassId()
    {
        string summary = CharacterClassWireShapeStudy.Describe("ksc", Ksc(), Ref);
        summary.Should().Contain("leaf=[f3:v,f6:len]");
        summary.Should().Contain("parent=[f3:v,f4:len]");
        summary.Should().Contain("between-characters-diff=[none]");
        summary.Should().Contain("class=unknown");
    }

    [Fact]
    public void KscDistinctIllustrativeFieldsAreReportedByPathOnly()
    {
        string summary = CharacterClassWireShapeStudy.Describe(
            "ksc", Ksc(withDifferentUnknownFields: true), Ref);

        summary.Should().Contain("leaf=[f1:v,f3:v,f6:len]");
        summary.Should().Contain("parent=[f3:v,f4:len,f5:v]");
        summary.Should().Contain("between-characters-diff=[leaf.f1:v,parent.f5:v]");
        summary.Should().Contain("class=unknown");
        summary.Should().NotContain("Sable-Ciel");
        summary.Should().NotContain("Roche-Lune");
        summary.Should().NotContain("9000123");
        summary.Should().NotContain("9000789");
    }

    [Fact]
    public void NeverTreatIdNameOrLevelDifferenceAsClassFieldCandidate()
    {
        string summary = CharacterClassWireShapeStudy.Describe("ksc", Ksc(), Ref);
        summary.Should().Contain("between-characters-diff=[none]");
        summary.Should().NotContain("leaf.f3:v");
        summary.Should().NotContain("leaf.f6:len");
        summary.Should().NotContain("parent.f3:v");
        summary.Should().NotContain("parent.f4:len");
    }

    [Fact]
    public void NonMatchingUserReferenceRemainsUnverified()
    {
        var other = new CharacterNameLevelReference("Other-Ciel", 137);
        CharacterClassWireShapeStudy.Describe("ksc", Ksc(), other)
            .Should().Be("p2g=reference-not-unique class=unknown");
    }

    [Fact]
    public void UnknownOrBrokenPayloadCannotProduceAClassShape()
    {
        CharacterClassWireShapeStudy.Describe("abc", Ksb(), Ref)
            .Should().Be("p2g=unavailable class=unknown");
        CharacterClassWireShapeStudy.Describe("ksb", [0], Ref)
            .Should().Be("p2g=unavailable class=unknown");
        CharacterClassWireShapeStudy.Describe(
            "ksc", new byte[8193], Ref)
            .Should().Be("p2g=unavailable class=unknown");
    }

    [Fact]
    public void DuplicateNamesInKscFailBeforeClassExploration()
    {
        byte[] bad = Message(
            Bytes(2, Row(9000789, "Sable-Ciel", 2)),
            Bytes(2, Row(9000123, "Sable-Ciel", 137)));
        CharacterClassWireShapeStudy.Describe("ksc", bad, Ref)
            .Should().Be("p2g=unavailable class=unknown");
    }

    [Fact]
    public void ProbeTrackerEmitsOnlyRedactedClassShapeForMatchedSelection()
    {
        var tracker = new CharacterCaptureFlowTracker(identityFieldsReference: Ref);
        var scope = new CharacterCaptureFlowKey(
            "127.0.0.1", 40001, "10.0.0.1", 5555);
        var timestamp = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        var list = tracker.Observe(
            scope, CharacterCaptureDirection.ServerToClient, 100,
            Frame(Any("ksc", Ksc(withDifferentUnknownFields: true))), timestamp)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);

        list.CharacterIdentityFieldSummary.Should().Contain(
            "p2g=observed leaf=[f1:v,f3:v,f6:len]");
        list.CharacterIdentityFieldSummary.Should().Contain(
            "between-characters-diff=[leaf.f1:v,parent.f5:v]");
        list.CharacterIdentityFieldSummary.Should().Contain("identity=unverified");
        list.CharacterIdentityFieldSummary.Should().NotContain("Sable-Ciel");
        list.CharacterIdentityFieldSummary.Should().NotContain("9000123");
    }

    [Fact]
    public void InvalidLocalReferenceIsRejected()
    {
        Action run = () => CharacterClassWireShapeStudy.Describe(
            "ksb", Ksb(), new CharacterNameLevelReference("Bad\nName", 137));
        run.Should().Throw<ArgumentException>();
    }
}
