using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>Regression: protobuf absent scalar f2 represents zero.</summary>
public sealed class ProfessionIpzZeroDefaultsTest
{
    private static byte[] Build(bool omitZero)
    {
        byte[][] rows = Enumerable.Range(0, 20)
            .Select(i => Bytes(1, Message(
                Unsigned(1, (ulong)(i < 15 ? i + 2 : 1)),
                omitZero && i >= 15 ? [] : Unsigned(2, (ulong)(i < 15 ? i * 11 : 0)),
                Unsigned(3, (ulong)(i + 500)))))
            .ToArray();

        return Message(rows);
    }

    [Fact]
    public void ImplicitZeroXpDoesNotRejectAnyOfTwentyRows()
    {
        ProfessionIpzDecoder.TryDecode(Build(omitZero: true), out var snapshot,
            out string? reason).Should().BeTrue();

        reason.Should().BeNull();
        snapshot!.Rows.Should().HaveCount(20);
        snapshot.ImplicitZeroExperienceRows.Should().Be(5);
        snapshot.Rows[19].Level.Should().Be(1);
        snapshot.Rows[19].ExperienceCounter.Should().Be(0);
        ProfessionIpzDecoder.DescribeAnonymously(snapshot,
            [new ProfessionReference(3, 11), new ProfessionReference(4, 22)])
            .Should().Contain("xp-implicit-zero=5");
    }

    [Fact]
    public void ExplicitZeroAndAbsentZeroDecodeToSameExperience()
    {
        ProfessionIpzDecoder.TryDecode(Build(omitZero: true), out var implicitXp)
            .Should().BeTrue();
        ProfessionIpzDecoder.TryDecode(Build(omitZero: false), out var explicitXp)
            .Should().BeTrue();

        implicitXp!.Rows.Select(x => x.ExperienceCounter)
            .Should().Equal(explicitXp!.Rows.Select(x => x.ExperienceCounter));
        implicitXp.ImplicitZeroExperienceRows.Should().Be(5);
        explicitXp.ImplicitZeroExperienceRows.Should().Be(0);
    }

    [Fact]
    public void MissingLevelStillFailsWithCategoricalReason()
    {
        byte[][] rows = Enumerable.Range(0, 20)
            .Select(i => Bytes(1, i == 10
                ? Unsigned(2, 777777)
                : Message(Unsigned(1, (ulong)(i + 1)),
                          Unsigned(2, (ulong)(i * 11)))))
            .ToArray();

        ProfessionIpzDecoder.TryDecode(Message(rows), out _,
            out string? reason).Should().BeFalse();
        reason.Should().Be("level-cardinality");
        reason.Should().NotContain("777777");
    }

    [Fact]
    public void DuplicateExperienceStillFailsSafely()
    {
        byte[][] rows = Enumerable.Range(0, 20)
            .Select(i => Bytes(1, i == 10
                ? Message(Unsigned(1, 11), Unsigned(2, 4), Unsigned(2, 5))
                : Message(Unsigned(1, (ulong)(i + 1)),
                          Unsigned(2, (ulong)(i * 11)))))
            .ToArray();

        ProfessionIpzDecoder.TryDecode(Message(rows), out _,
            out string? reason).Should().BeFalse();
        reason.Should().Be("xp-duplicate");
    }

    [Fact]
    public void TrackerAnonymizesZeroDefaultSummary()
    {
        var tracker = new CharacterCaptureFlowTracker(
            professionReferences: [new(3, 11), new(4, 22)],
            decodeIpz: true);
        DateTime now = new(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc);
        var entries = tracker.Observe(
            new("127.0.0.1", 41001, "10.0.0.1", 5555),
            CharacterCaptureDirection.ServerToClient,
            100, Frame(Any("ipz", Build(omitZero: true))), now);
        var message = entries.Single(e => e.Kind == CharacterCaptureEventKind.Message);
        message.ProfessionIpzSummary.Should().Contain("rows=20");
        message.ProfessionIpzSummary.Should().Contain("xp-implicit-zero=5");
        message.ProfessionIpzSummary.Should().NotContain("777777");
        message.ProfessionIpzSummary.Should().Contain("identity=unverified");
    }
}
