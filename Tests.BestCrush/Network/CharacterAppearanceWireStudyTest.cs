using System.Text;
using BestCrush.Network.Protocol;
using BestCrush.Network.Capture;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>All values and invented appearance fields are synthetic.</summary>
public sealed class CharacterAppearanceWireStudyTest
{
    private static readonly CharacterNameLevelReference Ref =
        new("Sable-Ciel", 137);

    private static byte[] Appearance(ulong type, ulong color) =>
        Message(
            Unsigned(1, type),
            Bytes(2, Message(Unsigned(1, color), Unsigned(2, 42))));

    private static byte[] Leaf(string name, int level, byte[]? appearance = null) =>
        Message(
            Unsigned(3, (ulong)level),
            appearance is null ? [] : Bytes(5, appearance),
            Bytes(6, Encoding.UTF8.GetBytes(name)));

    private static byte[] Row(
        ulong id, string name, int level, byte[]? appearance = null) =>
        Message(
            Unsigned(3, id),
            Bytes(4, Leaf(name, level, appearance)));

    private static byte[] Ksb(byte[]? appearance = null) =>
        Bytes(1, Bytes(1,
            Row(9000123, "Sable-Ciel", 137, appearance)));

    private static byte[] Ksc(
        byte[]? selected = null, byte[]? other = null) =>
        Message(
            Bytes(2, Row(9000789, "Roche-Lune", 2, other)),
            Bytes(2, Row(9000123, "Sable-Ciel", 137, selected)));

    [Fact]
    public void KscNestedAppearanceDifferencesDisplayPathsOnly()
    {
        byte[] selected = Appearance(6, 1200000);
        byte[] other = Appearance(17, 1300000);

        string summary = CharacterAppearanceWireStudy.Describe(
            "ksc", Ksc(selected, other), Ref);

        summary.Should().Contain(
            "p2h=f5-wire-shape=[f1:v,f2:len,f2.f1:v,f2.f2:v]");
        summary.Should().Contain("f5-other-diff=[f1:v,f2.f1:v]");
        summary.Should().Contain("class=unknown");
        foreach (string secret in new[] {
            "Sable-Ciel", "Roche-Lune", "9000123", "9000789",
            "1200000", "1300000"
        })
            summary.Should().NotContain(secret);
    }

    [Fact]
    public void KsbShowsShapeButCannotCompareToAnotherCharacter()
    {
        string summary = CharacterAppearanceWireStudy.Describe(
            "ksb", Ksb(Appearance(6, 1200000)), Ref);

        summary.Should().Contain("p2h=f5-wire-shape=[f1:v,f2:len,f2.f1:v,f2.f2:v]");
        summary.Should().Contain("f5-other-diff=[unavailable]");
        summary.Should().Contain("class=unknown");
    }

    [Fact]
    public void EqualAppearanceFieldsAreNotReportedAsDifferences()
    {
        byte[] appearance = Appearance(6, 1200000);
        string summary = CharacterAppearanceWireStudy.Describe(
            "ksc", Ksc(appearance, appearance), Ref);

        summary.Should().Contain("f5-other-diff=[none]");
        summary.Should().Contain("class=unknown");
    }

    [Fact]
    public void MissingF5NeverBecomesKnownClass()
    {
        string summary = CharacterAppearanceWireStudy.Describe(
            "ksc", Ksc(), Ref);
        summary.Should().Be("p2h=f5-missing class=unknown");
    }

    [Fact]
    public void NonProtobufF5IsTreatedAsOpaqueNotClass()
    {
        string summary = CharacterAppearanceWireStudy.Describe(
            "ksb", Ksb([0xff]), Ref);
        summary.Should().Be("p2h=f5-opaque class=unknown");
    }

    [Fact]
    public void OpaquePeerDoesNotLeadToSpeculativeFieldMatch()
    {
        string summary = CharacterAppearanceWireStudy.Describe(
            "ksc", Ksc(Appearance(6, 1200000), [0xff]), Ref);
        summary.Should().Contain("f5-other-diff=[opaque-peer]");
        summary.Should().Contain("class=unknown");
    }

    [Fact]
    public void UnmatchedNameAndMalformedBytesFailClosed()
    {
        var unknown = new CharacterNameLevelReference("Other-Ciel", 137);
        CharacterAppearanceWireStudy.Describe(
            "ksc", Ksc(Appearance(6, 1200000)), unknown)
            .Should().Be("p2h=reference-not-unique class=unknown");
        CharacterAppearanceWireStudy.Describe(
            "ksb", [0], Ref)
            .Should().Be("p2h=unavailable class=unknown");
        CharacterAppearanceWireStudy.Describe(
            "ksc", new byte[8193], Ref)
            .Should().Be("p2h=unavailable class=unknown");
        CharacterAppearanceWireStudy.Describe(
            "invalid", Ksb(), Ref)
            .Should().Be("p2h=unavailable class=unknown");
    }

    [Fact]
    public void TrackerShowsOnlyAnonymizedP2hFieldsInIdentityDiagnostic()
    {
        var tracker = new CharacterCaptureFlowTracker(identityFieldsReference: Ref);
        var scope = new CharacterCaptureFlowKey(
            "127.0.0.1", 40001, "10.0.0.1", 5555);
        var now = new DateTime(2026, 10, 18, 13, 0, 0, DateTimeKind.Utc);

        var output = tracker.Observe(
            scope, CharacterCaptureDirection.ServerToClient, 100,
            Frame(Any("ksc", Ksc(Appearance(6, 1200000),
                Appearance(17, 1300000)))), now)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);

        output.CharacterIdentityFieldSummary.Should().Contain(
            "p2h=f5-wire-shape=[f1:v,f2:len,f2.f1:v,f2.f2:v]");
        output.CharacterIdentityFieldSummary.Should().Contain(
            "f5-other-diff=[f1:v,f2.f1:v]");
        output.CharacterIdentityFieldSummary.Should().NotContain("Sable-Ciel");
        output.CharacterIdentityFieldSummary.Should().NotContain("9000123");
        output.CharacterIdentityFieldSummary.Should().Contain("identity=unverified");
    }

    [Fact]
    public void InvalidReferenceIsRejected()
    {
        Action a = () => CharacterAppearanceWireStudy.Describe(
            "ksb", Ksb(), new CharacterNameLevelReference("Bad\nName", 137));
        a.Should().Throw<ArgumentException>();
    }
}
