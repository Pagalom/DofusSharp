using BestCrush.Services;
using FluentAssertions;

namespace Tests.BestCrush.Network;

public sealed class NetworkFlowCacheTest
{
    private static readonly DateTime T0 =
        new(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);

    private static readonly NetworkCaptureLease ServerA =
        new("ServerA", 1);

    [Fact]
    public void SameActiveFlowKeepsItsReassembler()
    {
        NetworkFlowCache<string, object> cache =
            new(capacity: 2, idleTimeout: TimeSpan.FromMinutes(10));

        object reader = cache.GetOrCreate("flow1", ServerA, T0, () => new object());
        object next = cache.GetOrCreate(
            "flow1", ServerA, T0.AddMinutes(3), () => new object());

        next.Should().BeSameAs(reader);
        cache.Count.Should().Be(1);
    }

    [Fact]
    public void IdleFlowIsRecreatedRatherThanReusingOldSequenceState()
    {
        NetworkFlowCache<string, object> cache =
            new(capacity: 2, idleTimeout: TimeSpan.FromMinutes(10));

        object old = cache.GetOrCreate("flow1", ServerA, T0, () => new object());
        object fresh = cache.GetOrCreate(
            "flow1", ServerA, T0.AddMinutes(10), () => new object());

        fresh.Should().NotBeSameAs(old);
        cache.Count.Should().Be(1);
    }

    [Fact]
    public void CacheEvictsOldestFlowBeforeExceedingItsCapacity()
    {
        NetworkFlowCache<string, object> cache =
            new(capacity: 2, idleTimeout: TimeSpan.FromMinutes(30));

        object first = cache.GetOrCreate("first", ServerA, T0, () => new object());
        object second = cache.GetOrCreate(
            "second", ServerA, T0.AddSeconds(1), () => new object());

        cache.GetOrCreate("first", ServerA, T0.AddSeconds(2), () => new object())
            .Should().BeSameAs(first);

        cache.GetOrCreate(
            "third", ServerA, T0.AddSeconds(3), () => new object());

        cache.Count.Should().Be(2);
        cache.GetOrCreate(
            "first", ServerA, T0.AddSeconds(4), () => new object())
            .Should().BeSameAs(first);

        cache.GetOrCreate(
            "second", ServerA, T0.AddSeconds(5), () => new object())
            .Should().NotBeSameAs(second);

        cache.Count.Should().Be(2);
    }

    [Fact]
    public void OldServerLeaseNeverReusesItsPreviousReader()
    {
        NetworkFlowCache<string, object> cache =
            new(capacity: 3, idleTimeout: TimeSpan.FromMinutes(30));

        object old = cache.GetOrCreate(
            "flow1", ServerA, T0, () => new object());

        NetworkCaptureLease newLease = new("ServerB", 2);
        object fresh = cache.GetOrCreate(
            "flow1", newLease, T0.AddSeconds(1), () => new object());

        fresh.Should().NotBeSameAs(old);
        cache.Count.Should().Be(1);
        cache.Clear();
        cache.Count.Should().Be(0);
    }
}
