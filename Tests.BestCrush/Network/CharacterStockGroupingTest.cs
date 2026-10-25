using BestCrush.Services;
using FluentAssertions;

namespace Tests.BestCrush.Network;

public sealed class CharacterStockGroupingTest
{
    private static CharacterInventoryItem Item(
        ulong itemId, ulong? uid, long quantity,
        params CharacterInventoryEffect[] effects) =>
        new(itemId, uid, quantity) { Effects = effects };

    [Fact]
    public void SameTypeAndSameExactJetsMergeIndependentlyOfUid()
    {
        CharacterInventoryItem[] inventory =
        [
            Item(2427, 111, 1,
                new CharacterInventoryEffect(1001, 24),
                new CharacterInventoryEffect(1002, 26)),
            Item(2427, 222, 2,
                new CharacterInventoryEffect(1002, 26),
                new CharacterInventoryEffect(1001, 24)),
            Item(2427, 333, 1,
                new CharacterInventoryEffect(1001, 25),
                new CharacterInventoryEffect(1002, 26)),
            Item(1523, 444, 7),
            Item(1523, 555, 3)
        ];

        IReadOnlyList<CharacterStockGroup>? groups =
            CharacterStockGrouping.TryGroup(inventory);
        groups.Should().NotBeNull();
        groups.Should().HaveCount(3);
        groups!.Where(x => x.ItemId == 2427)
            .Select(x => x.Quantity)
            .Should().BeEquivalentTo(new long[] { 3, 1 });
        groups.Single(x => x.ItemId == 1523).Quantity.Should().Be(10);
        groups.SelectMany(x => x.Effects)
            .Should().NotContain(x => x.EffectId == 0);

        // Raw instance references remain untouched; projection does not
        // track the "same" item across a bank or inventory transaction.
        inventory.Select(x => x.InstanceUid)
            .Should().BeEquivalentTo(new ulong?[] { 111, 222, 333, 444, 555 });
    }

    [Fact]
    public void ASmithmagicOrPetFeedingChangeMovesToAnotherFunctionalGroup()
    {
        CharacterInventoryItem[] inventory =
        [
            Item(2427, 111, 1, new CharacterInventoryEffect(1001, 24)),
            Item(2427, 222, 1, new CharacterInventoryEffect(1001, 25))
        ];
        CharacterStockGrouping.TryGroup(inventory)!.Should().HaveCount(2);

        CharacterInventoryItem updated = inventory[0] with
        {
            Effects = [new CharacterInventoryEffect(1001, 25)]
        };
        IReadOnlyList<CharacterStockGroup> merged =
            CharacterStockGrouping.TryGroup([updated, inventory[1]])!;
        merged.Should().ContainSingle()
            .Which.Quantity.Should().Be(2);
    }

    [Fact]
    public void NullEffectsAreUnknownAndNotEquivalentToKnownEmptyEffects()
    {
        CharacterStockGrouping.TryGroup(null).Should().BeNull();
        CharacterStockGrouping.TryGroup([]).Should().BeEmpty();

        CharacterInventoryItem unknown = new(1523, 111, 10);
        CharacterStockGrouping.TryGroup([unknown]).Should().BeNull();
        CharacterStockGrouping.TryGroup([Item(1523, 222, 10)])
            .Should().ContainSingle()
            .Which.Effects.Should().BeEmpty();
        CharacterStockGrouping.TryGroup(
            [unknown, Item(1523, 222, 10)]).Should().BeNull();
    }

    [Fact]
    public void InvalidQuantityOverflowAndInvalidEffectFailClosed()
    {
        CharacterStockGrouping.TryGroup([Item(0, 111, 1)])
            .Should().BeNull();
        CharacterStockGrouping.TryGroup([Item(1523, 111, 0)])
            .Should().BeNull();
        CharacterStockGrouping.TryGroup([Item(1523, 111, -1)])
            .Should().BeNull();
        CharacterStockGrouping.TryGroup(
            [Item(1523, 111, 1, new CharacterInventoryEffect(0, 3))])
            .Should().BeNull();
        CharacterStockGrouping.TryGroup(
        [
            Item(1523, 111, long.MaxValue),
            Item(1523, 222, 1)
        ]).Should().BeNull();
    }

    [Fact]
    public void SeparateContainersHaveSeparateViewsEvenWithEqualUidsAndTypes()
    {
        // Each known owner/container passes its own snapshot explicitly.
        // The grouping helper must never silently combine input from both.
        CharacterInventoryItem[] inventory =
            [Item(2427, 111, 2, new CharacterInventoryEffect(1001, 24))];
        CharacterInventoryItem[] bank =
            [Item(2427, 111, 7, new CharacterInventoryEffect(1001, 24))];

        CharacterStockGrouping.TryGroup(inventory)!.Single()
            .Quantity.Should().Be(2);
        CharacterStockGrouping.TryGroup(bank)!.Single()
            .Quantity.Should().Be(7);
    }

    [Fact]
    public void RepeatedEffectsAndSignedValuesRemainPartOfTheGroupKey()
    {
        CharacterInventoryItem[] data =
        [
            Item(2427, 1, 1,
                new CharacterInventoryEffect(1001, -2),
                new CharacterInventoryEffect(1001, 5)),
            Item(2427, 2, 1,
                new CharacterInventoryEffect(1001, 5),
                new CharacterInventoryEffect(1001, -2)),
            Item(2427, 3, 1,
                new CharacterInventoryEffect(1001, 5))
        ];
        IReadOnlyList<CharacterStockGroup> groups =
            CharacterStockGrouping.TryGroup(data)!;
        groups.Should().HaveCount(2);
        groups.Select(x => x.Quantity)
            .Should().BeEquivalentTo(new long[] { 2, 1 });
    }
}
