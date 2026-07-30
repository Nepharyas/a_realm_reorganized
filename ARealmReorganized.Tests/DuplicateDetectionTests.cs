using ARealmReorganized.Logic;
using ARealmReorganized.Models;

namespace ARealmReorganized.Tests;

public class DuplicateDetectionTests
{
    private static DresserItem Dresser(uint itemId, ushort slot, byte stain0 = 0, byte stain1 = 0) =>
        new(itemId, slot, stain0, stain1);

    private static InventoryEntry Bag(uint itemId, InventorySource source = InventorySource.Inventory, bool isHq = false) =>
        new(itemId, source, isHq);

    private static RetainerInventoryCache Retainer(string name, params uint[] itemIds)
    {
        var cache = new RetainerInventoryCache { Name = name };
        foreach (var itemId in itemIds)
            cache.Entries.Add(new CachedInventoryEntry { ItemId = itemId });
        return cache;
    }

    private static DuplicateDetection.Result Find(
        IEnumerable<DresserItem>? dresser = null,
        IEnumerable<InventoryEntry>? bags = null,
        Dictionary<ulong, RetainerInventoryCache>? retainers = null,
        Func<uint, bool>? isInArmoire = null,
        Func<uint, bool>? isGear = null) =>
        DuplicateDetection.Find(
            dresser ?? [],
            bags ?? [],
            retainers ?? [],
            isInArmoire ?? (_ => false),
            isGear ?? (_ => true));

    [Fact]
    public void TwoDresserCopies_AreMultiple()
    {
        var result = Find(dresser: [Dresser(1, 0), Dresser(1, 1)]);

        var item = Assert.Single(result.MultipleCopies);
        Assert.Equal(1u, item.ItemId);
        Assert.Equal(2, item.TotalCopies);
        Assert.Equal(2, item.DresserCopies.Count);
        Assert.Empty(result.ArmoireRedundant);
    }

    [Fact]
    public void DresserPlusRetainerCopy_AreMultiple()
    {
        var result = Find(
            dresser: [Dresser(1, 0)],
            retainers: new() { [42] = Retainer("Bob", 1) });

        var item = Assert.Single(result.MultipleCopies);
        Assert.Equal(2, item.TotalCopies);
        var retainerCopy = Assert.Single(item.RetainerCopies);
        Assert.Equal("Bob", retainerCopy.RetainerName);
        Assert.Equal(42u, retainerCopy.RetainerId);
        Assert.Equal(1, retainerCopy.Count);
    }

    [Fact]
    public void BagAndSaddlebagCopies_CountPerSource()
    {
        var result = Find(bags:
        [
            Bag(1), Bag(1, InventorySource.Saddlebag), Bag(1, InventorySource.Saddlebag),
        ]);

        var item = Assert.Single(result.MultipleCopies);
        Assert.Equal(3, item.TotalCopies);
        Assert.Equal(1, item.BagCopies[InventorySource.Inventory]);
        Assert.Equal(2, item.BagCopies[InventorySource.Saddlebag]);
    }

    [Fact]
    public void UndyedDresserCopy_AlreadyStored_IsArmoireRedundant()
    {
        var result = Find(
            dresser: [Dresser(1, 0)],
            isInArmoire: id => id == 1);

        var item = Assert.Single(result.ArmoireRedundant);
        Assert.True(item.InArmoire);
        Assert.Equal(1, item.TotalCopies);
        Assert.Empty(result.MultipleCopies);
    }

    [Fact]
    public void DyedDresserCopy_AlreadyStored_IsNotRedundant()
    {
        var result = Find(
            dresser: [Dresser(1, 0, stain0: 5)],
            isInArmoire: _ => true);

        Assert.Empty(result.ArmoireRedundant);
        Assert.Empty(result.MultipleCopies);
    }

    [Fact]
    public void BagCopy_AlreadyStored_IsArmoireRedundant()
    {
        var result = Find(bags: [Bag(1)], isInArmoire: _ => true);

        var item = Assert.Single(result.ArmoireRedundant);
        Assert.Equal(1, item.BagCopies[InventorySource.Inventory]);
    }

    [Fact]
    public void NonGearBagEntries_AreIgnored()
    {
        var result = Find(
            bags: [Bag(1), Bag(1)],
            isGear: _ => false);

        Assert.Empty(result.MultipleCopies);
        Assert.Empty(result.ArmoireRedundant);
    }

    [Fact]
    public void ArmoireOnly_WithNoCopiesInHand_IsNotListed()
    {
        var result = Find(isInArmoire: _ => true);

        Assert.Empty(result.MultipleCopies);
        Assert.Empty(result.ArmoireRedundant);
    }

    [Fact]
    public void MultipleCopies_AlsoStored_StayInMultipleWithArmoireFlag()
    {
        var result = Find(
            dresser: [Dresser(1, 0), Dresser(1, 1)],
            isInArmoire: _ => true);

        var item = Assert.Single(result.MultipleCopies);
        Assert.True(item.InArmoire);
        Assert.Empty(result.ArmoireRedundant);
    }

    [Fact]
    public void ResultsAreSortedByItemId()
    {
        var result = Find(dresser:
        [
            Dresser(9, 0), Dresser(9, 1),
            Dresser(2, 2), Dresser(2, 3),
        ]);

        Assert.Equal(2u, result.MultipleCopies[0].ItemId);
        Assert.Equal(9u, result.MultipleCopies[1].ItemId);
    }
}
