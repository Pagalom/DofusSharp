using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>Only synthetic data: tests never call DofusDb or disclose wire IDs.</summary>
public sealed class ItemEffectDofusDbCatalogStudyTest
{
    private static ItemEffectDofusDbCatalogStudy NewStudy() => new(
        new ItemCatalogCandidateReference("A", [99, 20],
        [
            new(1001, 80, 100),
            new(1002, 16, 20),
            // The public item catalog may contain an effect shown at zero.
            new(1005, 0, 5)
        ]),
        new ItemCatalogCandidateReference("B", [24, 26, 5, 3],
        [
            new(1001, 21, 30),
            new(1002, 21, 30),
            new(1003, 5, 5),
            new(1004, 2, 3)
        ]));

    private static byte[] Item(params (long Value, ulong Code)[] effects) =>
        Message(Unsigned(3, 1), Unsigned(4, 2418), Unsigned(5, 54321),
            Message(effects.Select(x => Bytes(7,
                Message(Unsigned(5, unchecked((ulong)x.Value)),
                    Unsigned(10, x.Code)))).ToArray()));

    private static byte[] Iqx(params (long Value, ulong Code)[] effects) =>
        Bytes(2, Bytes(1, Item(effects)));

    private static byte[] Iqz(params (long Value, ulong Code)[] effects) =>
        Bytes(1, Bytes(1, Item(effects)));

    [Fact]
    public void KnownEffectsMatchPublicCatalogWithoutRequiringZeroValueEffects()
    {
        ItemEffectDofusDbCatalogStudy study = NewStudy();
        study.Observe("iqx", Iqx((99, 1001), (20, 1002)))
            .Should().Be("item=A f10-vs-dofusdb-effectId=all " +
                "value-ranges=all catalog-effects=3 wire-effects=2 " +
                "catalog-version=unverified");
        study.Observe("iqz", Iqz(
            (5, 1003), (26, 1002), (3, 1004), (24, 1001)))
            .Should().Be("item=B f10-vs-dofusdb-effectId=all " +
                "value-ranges=all catalog-effects=4 wire-effects=4 " +
                "catalog-version=unverified");
    }

    [Fact]
    public void UnknownEffectCodeIsNotClaimedAsCompatible()
    {
        ItemEffectDofusDbCatalogStudy study = NewStudy();
        study.Observe("iqx", Iqx((99, 4444), (20, 1002)))
            .Should().Contain("f10-vs-dofusdb-effectId=partial");
        study.Observe("iqx", Iqx((99, 4444), (20, 5555)))
            .Should().Contain("f10-vs-dofusdb-effectId=none");
    }

    [Fact]
    public void CatalogVersionDifferencesAreDetectedViaRanges()
    {
        ItemEffectDofusDbCatalogStudy study = new(
            new ItemCatalogCandidateReference("A", [99, 20],
                [new(1001, 1, 50), new(1002, 1, 30)]),
            new ItemCatalogCandidateReference("B", [24, 26, 5, 3],
                [new(1001, 1, 50), new(1002, 1, 30),
                 new(1003, 1, 10), new(1004, 1, 5)]));

        study.Observe("iqx", Iqx((99, 1001), (20, 1002)))
            .Should().Contain("f10-vs-dofusdb-effectId=all value-ranges=partial");
    }

    [Fact]
    public void MissingRangesAreNotPassedOffAsValidated()
    {
        ItemEffectDofusDbCatalogStudy study = new(
            new ItemCatalogCandidateReference("A", [99, 20],
                [new(1001, null, null), new(1002, null, null)]),
            new ItemCatalogCandidateReference("B", [24, 26, 5, 3],
                [new(1001, null, null), new(1002, null, null),
                 new(1003, null, null), new(1004, null, null)]));

        study.Observe("iqx", Iqx((99, 1001), (20, 1002)))
            .Should().Contain("value-ranges=unknown");
    }

    [Fact]
    public void OtherItemsAndIncompletePayloadsAreIgnored()
    {
        ItemEffectDofusDbCatalogStudy study = NewStudy();
        study.Observe("iqx", Iqx((99, 1001))).Should().BeNull();
        study.Observe("iqx", Iqx((99, 1001), (18, 1002))).Should().BeNull();
        study.Observe("not-item", Iqx((99, 1001), (20, 1002)))
            .Should().BeNull();
        study.Observe("iqx", [0x00]).Should().BeNull();
    }

    [Fact]
    public void ReferencesMustBeUniqueAndNonzeroAndHaveCatalogData()
    {
        Action a = () => new ItemEffectDofusDbCatalogStudy(
            new ItemCatalogCandidateReference("A", [99, 99],
                [new(1001, 1, 100)]),
            new ItemCatalogCandidateReference("B", [24, 26, 5, 3],
                [new(1001, 1, 100)]));
        a.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TheTrackerIsOptInAndCannotExposeCatalogIdsOrWireValues()
    {
        ItemEffectDofusDbCatalogStudy study = NewStudy();
        CharacterCaptureFlowTracker tracker = new(
            itemEffectsShape: true, effectCatalogStudy: study);
        CharacterCaptureFlowKey flow = new(
            "127.0.0.1", 40001, "10.0.0.10", 5555);
        DateTime t = new(2026, 10, 24, 22, 0, 0, DateTimeKind.Utc);
        byte[] frame = Frame(Any("iqx", Iqx((99, 1001), (20, 1002))));
        CharacterCaptureEvent result = tracker.Observe(flow,
            CharacterCaptureDirection.ServerToClient, 100, frame, t)
            .Single(x => x.Kind == CharacterCaptureEventKind.Message);
        result.ItemEffectsShape.Should().Contain(
            "f10-vs-dofusdb-effectId=all");
        string json = System.Text.Json.JsonSerializer.Serialize(result);
        foreach (string privateValue in new[] { "1001", "1002", "54321", "2418" })
            json.Should().NotContain(privateValue);

        Action bad = () => new CharacterCaptureFlowTracker(
            effectCatalogStudy: study);
        bad.Should().Throw<ArgumentException>();
    }
}
