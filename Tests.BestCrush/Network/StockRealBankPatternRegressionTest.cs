using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>
/// Characterizes the real 2026-10-25 P3d.15 observation with synthetic
/// identifiers and effect values. NO real bank data is reproduced here.
/// 560 complete rows: 157 with effects, 60 with non-numeric effect shapes.
/// A 3-unit partial withdrawal must register one decreased structural group.
/// </summary>
public sealed class StockRealBankPatternRegressionTest
{
    private static byte[] EffectForRow(int i) => i switch
    {
        < 36 => Message(Bytes(9, [8, 1]), Unsigned(10, 601)),
        < 45 => Message(Bytes(7, [8, 2]), Unsigned(10, 602)),
        < 52 => Message(Unsigned(10, 603)),
        < 58 => Message(Bytes(8, [8, 3]), Unsigned(10, 604)),
        < 60 => Message(Bytes(6, [8, 4]), Unsigned(10, 605)),
        // 97 rows with ordinary scalar effects: 60+97 = 157
        < 157 => Message(Unsigned(5, 24), Unsigned(10, 606)),
        _ => []
    };

    private static byte[] Snapshot(int changedQuantity, ulong uidOffset)
    {
        byte[][] rows = Enumerable.Range(0, 560).Select(i =>
        {
            byte[] effect = EffectForRow(i);
            byte[] effects = effect.Length == 0
                ? [] : Bytes(7, effect);
            return Bytes(2, Bytes(1, Message(
                Unsigned(3, (ulong)(i == 400 ? changedQuantity : 1)),
                Unsigned(4, (ulong)(50_000 + i)),
                Unsigned(5, uidOffset + (ulong)i),
                effects)));
        }).ToArray();
        return Message(Unsigned(1, 1), Message(rows));
    }

    [Fact]
    public void LargeBankSnapshotFindsOneDecreaseDespiteSixtySpecialEffectRows()
    {
        StockSnapshotDeltaStudy study = new();
        string first = study.Observe(1, "irp", Snapshot(10, 700_000))!;
        string second = study.Observe(1, "irp", Snapshot(7, 900_000))!;

        first.Should().Contain("container=bank status=complete-candidate rows=560 fields345=560");
        first.Should().Contain("effects-present=157 duplicate-f5=0");
        first.Should().Contain("grouping=not-ready snapshot-change=not-comparable");
        first.Should().Contain(
            "wire-grouping=candidate groups=560 source-rows=560 rows-with-structured-effects=60");
        first.Should().Contain("wire-snapshot-change=no-prior-snapshot");
        first.Should().Contain("undecodable-effects:60");
        first.Should().Contain("effect-shapes:[missing-value:60]");

        second.Should().Contain(
            "wire-snapshot-change=compared increased-groups=0 decreased-groups=1 " +
            "new-groups=0 removed-groups=0 unchanged-groups=559");
        second.Should().Contain("grouping=not-ready");
        second.Should().Contain("rows-with-structured-effects=60");

        // Synthetic item/UID/effect values remain private. Opt-in diagnostic
        // is deliberately not enabled in this regression.
        foreach (string report in new[] { first, second })
        foreach (string secret in new[]
        {
            "50000", "50400", "50559", "700000", "900000",
            "item-id-candidate:", "blocked-type-shapes="
        })
            report.Should().NotContain(secret);
    }
}
