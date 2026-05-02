using ARealmReorganized.Logic;
using ARealmReorganized.Models;

namespace ARealmReorganized.Tests;

public class DuplicateDetectionResultTests
{
    private static DresserItem Item(uint itemId, ushort slot) => new(itemId, slot, 0, 0);

    private static DuplicateDetection.Result MakeResult(
        IReadOnlyList<DresserItem>? armoireRedundant = null,
        IReadOnlyList<DresserItem>? multipleCopies = null) =>
        new()
        {
            ArmoireRedundant = armoireRedundant ?? [],
            MultipleCopies = multipleCopies ?? [],
        };

    [Fact]
    public void WithSlotsRemoved_EmptySet_ReturnsSameInstance()
    {
        var result = MakeResult(
            armoireRedundant: [Item(1, 0)],
            multipleCopies: [Item(2, 1), Item(2, 2)]);

        var updated = result.WithSlotsRemoved([]);

        Assert.Same(result, updated);
    }

    [Fact]
    public void WithSlotsRemoved_RemovesMatchingArmoireRedundantItem()
    {
        var result = MakeResult(armoireRedundant: [Item(1, 0), Item(2, 1)]);

        var updated = result.WithSlotsRemoved([0]);

        Assert.Equal([Item(2, 1)], updated.ArmoireRedundant);
    }

    [Fact]
    public void WithSlotsRemoved_RemovesMatchingMultipleCopiesItem()
    {
        var result = MakeResult(multipleCopies: [Item(1, 0), Item(1, 1), Item(2, 2), Item(2, 3)]);

        var updated = result.WithSlotsRemoved([0, 2]);

        // item 1 slot 0 removed, item 1 slot 1 stays alone → dropped (no longer a duplicate)
        // item 2 slot 2 removed, item 2 slot 3 stays alone → dropped
        Assert.Empty(updated.MultipleCopies);
    }

    [Fact]
    public void WithSlotsRemoved_KeepsPairWhenOnlyOneOfThreeCopiesRemoved()
    {
        var result = MakeResult(multipleCopies: [Item(1, 0), Item(1, 1), Item(1, 2)]);

        var updated = result.WithSlotsRemoved([0]);

        Assert.Equal([Item(1, 1), Item(1, 2)], updated.MultipleCopies);
    }

    [Fact]
    public void WithSlotsRemoved_DropsRemainingCopyWhenPairIsReducedToOne()
    {
        var result = MakeResult(multipleCopies: [Item(1, 0), Item(1, 1)]);

        var updated = result.WithSlotsRemoved([0]);

        Assert.Empty(updated.MultipleCopies);
    }

    [Fact]
    public void WithSlotsRemoved_UnrelatedItemsAreUntouched()
    {
        var result = MakeResult(
            armoireRedundant: [Item(10, 5)],
            multipleCopies: [Item(20, 6), Item(20, 7)]);

        var updated = result.WithSlotsRemoved([99]);

        Assert.Equal([Item(10, 5)], updated.ArmoireRedundant);
        Assert.Equal([Item(20, 6), Item(20, 7)], updated.MultipleCopies);
    }

    [Fact]
    public void WithSlotsRemoved_RemovesBothListsSimultaneously()
    {
        var result = MakeResult(
            armoireRedundant: [Item(1, 0), Item(2, 1)],
            multipleCopies: [Item(3, 2), Item(3, 3), Item(4, 4), Item(4, 5)]);

        var updated = result.WithSlotsRemoved([0, 2]);

        Assert.Equal([Item(2, 1)], updated.ArmoireRedundant);
        // slot 2 removed → item 3 slot 3 alone → dropped; item 4 pair intact
        Assert.Equal([Item(4, 4), Item(4, 5)], updated.MultipleCopies);
    }
}
