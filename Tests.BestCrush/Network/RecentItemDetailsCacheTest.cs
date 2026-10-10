using BestCrush.Network.Protocol;
using BestCrush.Services;
using FluentAssertions;

namespace Tests.BestCrush.Network;

public sealed class RecentItemDetailsCacheTest
{
    [Fact]
    public void OldestUidIsEvictedWhenCapacityIsReached()
    {
        RecentItemDetailsCache cache = new(capacity: 2);
        cache.Remember(Item(10, 42));
        cache.Remember(Item(11, 43));
        cache.Remember(Item(12, 44));

        cache.Count.Should().Be(2);
        cache.TryGetValue(10, out _).Should().BeFalse();
        cache.TryGetValue(11, out _).Should().BeTrue();
        cache.TryGetValue(12, out _).Should().BeTrue();
    }

    [Fact]
    public void RecentlyUsedUidIsProtectedFromEviction()
    {
        RecentItemDetailsCache cache = new(capacity: 2);
        cache.Remember(Item(10, 42));
        cache.Remember(Item(11, 43));

        cache.TryGetValue(10, out ItemDetailObservation? used)
            .Should().BeTrue();
        used!.ItemId.Should().Be(42);

        cache.Remember(Item(12, 44));

        cache.TryGetValue(10, out _).Should().BeTrue();
        cache.TryGetValue(11, out _).Should().BeFalse();
    }

    [Fact]
    public void UpdatedUidReplacesContentWithoutGrowingTheCache()
    {
        RecentItemDetailsCache cache = new(capacity: 2);
        cache.Remember(Item(10, 42));
        cache.Remember(Item(11, 43));
        cache.Remember(Item(10, 45));
        cache.Remember(Item(12, 44));

        cache.Count.Should().Be(2);
        cache.Items[10].ItemId.Should().Be(45);
        cache.TryGetValue(11, out _).Should().BeFalse();
    }

    [Fact]
    public void ClearReleasesAllCorrelationsBetweenCaptureGenerations()
    {
        RecentItemDetailsCache cache = new(capacity: 2);
        cache.Remember(Item(10, 42));
        cache.Clear();

        cache.Count.Should().Be(0);
        cache.TryGetValue(10, out _).Should().BeFalse();
        cache.Items.Should().BeEmpty();
        cache.Remember(Item(11, 43));
        cache.Count.Should().Be(1);
    }

    private static ItemDetailObservation Item(ulong uid, ulong itemId) =>
        new(uid, itemId, 1, []);
}
