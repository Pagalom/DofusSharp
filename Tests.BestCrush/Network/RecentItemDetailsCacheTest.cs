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

    [Fact]
    public void CrushUsesRemovedEquipmentEvenWhenYieldedRuneReusesItsUid()
    {
        var cache=new RecentItemDetailsCache();var at=DateTime.UtcNow;
        cache.Remember(Item(8001,42));cache.RememberRemoval(8001,at);
        cache.Remember(Item(8001,100));
        Assert.Equal(42UL,cache.TakeForCrush([8001],at.AddMilliseconds(1))[8001].ItemId);
        Assert.Equal(100UL,cache.Items[8001].ItemId);
        Assert.Equal(100UL,cache.TakeForCrush([8001],at.AddMilliseconds(2))[8001].ItemId);
    }
    [Fact]
    public void RemovedIdentityIsBoundedInTimeAndClearedWithConnectionCache()
    {
        var cache=new RecentItemDetailsCache();var at=DateTime.UtcNow;
        cache.Remember(Item(8001,42));cache.RememberRemoval(8001,at);cache.Remember(Item(8001,100));
        Assert.Equal(100UL,cache.TakeForCrush([8001],at.AddSeconds(16))[8001].ItemId);
        cache.RememberRemoval(8001,at.AddSeconds(17));cache.Clear();
        Assert.Empty(cache.TakeForCrush([8001],at.AddSeconds(18)));
    }
    private static ItemDetailObservation Item(ulong uid, ulong itemId) =>
        new(uid, itemId, 1, []);
}
