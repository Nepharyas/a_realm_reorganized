using ARealmReorganized.Logic;
using ARealmReorganized.Models;

namespace ARealmReorganized.Tests;

public class InventoryGroupingTests
{
    private static InventoryEntry Entry(uint itemId, InventorySource source, bool isHq = false) =>
        new(itemId, source, isHq);

    private static bool KeepAll(InventoryEntry _) => true;

    [Fact]
    public void FilterAndGroup_DropsItemsThatFailPredicate()
    {
        InventoryEntry[] entries =
        [
            Entry(1, InventorySource.Inventory),
            Entry(2, InventorySource.Inventory),
            Entry(3, InventorySource.Inventory),
        ];

        var result = InventoryGrouping.FilterAndGroup(entries, entry => entry.ItemId != 2);

        Assert.Equal(
            [Entry(1, InventorySource.Inventory), Entry(3, InventorySource.Inventory)],
            result.Deduped);
    }

    [Fact]
    public void FilterAndGroup_DedupesByItemIdKeepingFirstSeenSource()
    {
        InventoryEntry[] entries =
        [
            Entry(1, InventorySource.Inventory),
            Entry(1, InventorySource.Saddlebag),
            Entry(1, InventorySource.Armoury),
        ];

        var result = InventoryGrouping.FilterAndGroup(entries, KeepAll);

        var only = Assert.Single(result.Deduped);
        Assert.Equal(Entry(1, InventorySource.Inventory), only);
    }

    [Fact]
    public void FilterAndGroup_GroupsByOriginalSource()
    {
        InventoryEntry[] entries =
        [
            Entry(1, InventorySource.Inventory),
            Entry(2, InventorySource.Saddlebag),
            Entry(3, InventorySource.Inventory),
            Entry(4, InventorySource.Armoury),
        ];

        var result = InventoryGrouping.FilterAndGroup(entries, KeepAll);

        Assert.Equal(
            [Entry(1, InventorySource.Inventory), Entry(3, InventorySource.Inventory)],
            result.BySource[InventorySource.Inventory]);
        Assert.Equal(
            [Entry(2, InventorySource.Saddlebag)],
            result.BySource[InventorySource.Saddlebag]);
        Assert.Equal(
            [Entry(4, InventorySource.Armoury)],
            result.BySource[InventorySource.Armoury]);
    }

    [Fact]
    public void FilterAndGroup_OmitsEmptySourcesFromBySource()
    {
        InventoryEntry[] entries =
        [
            Entry(1, InventorySource.Inventory),
        ];

        var result = InventoryGrouping.FilterAndGroup(entries, KeepAll);

        Assert.Single(result.BySource);
        Assert.True(result.BySource.ContainsKey(InventorySource.Inventory));
        Assert.False(result.BySource.ContainsKey(InventorySource.Saddlebag));
        Assert.False(result.BySource.ContainsKey(InventorySource.Armoury));
    }

    [Fact]
    public void FilterAndGroup_EmptyInputReturnsEmptyResult()
    {
        var result = InventoryGrouping.FilterAndGroup([], KeepAll);

        Assert.Empty(result.Deduped);
        Assert.Empty(result.BySource);
    }

    [Fact]
    public void FilterAndGroup_DedupedAndBySourceAgreeOnContents()
    {
        InventoryEntry[] entries =
        [
            Entry(10, InventorySource.Inventory),
            Entry(10, InventorySource.Saddlebag),
            Entry(20, InventorySource.Saddlebag),
            Entry(30, InventorySource.Armoury),
        ];

        var result = InventoryGrouping.FilterAndGroup(entries, KeepAll);

        var flatFromGroups = result.BySource.SelectMany(kv => kv.Value).ToHashSet();
        Assert.Equal(result.Deduped.ToHashSet(), flatFromGroups);
    }

    // --- HQ promotion ---

    [Fact]
    public void FilterAndGroup_PromotesIsHqWhenAnyCopyIsHq()
    {
        InventoryEntry[] entries =
        [
            Entry(1, InventorySource.Inventory, isHq: false),
            Entry(1, InventorySource.Saddlebag, isHq: true),
        ];

        var result = InventoryGrouping.FilterAndGroup(entries, KeepAll);

        var only = Assert.Single(result.Deduped);
        Assert.True(only.IsHq);
        Assert.Equal(InventorySource.Inventory, only.Source);
    }

    [Fact]
    public void FilterAndGroup_HqOnlyEntryStaysHq()
    {
        InventoryEntry[] entries =
        [
            Entry(1, InventorySource.Inventory, isHq: true),
        ];

        var result = InventoryGrouping.FilterAndGroup(entries, KeepAll);

        var only = Assert.Single(result.Deduped);
        Assert.True(only.IsHq);
    }

    [Fact]
    public void FilterAndGroup_NqOnlyEntryStaysNq()
    {
        InventoryEntry[] entries =
        [
            Entry(1, InventorySource.Inventory, isHq: false),
            Entry(1, InventorySource.Saddlebag, isHq: false),
        ];

        var result = InventoryGrouping.FilterAndGroup(entries, KeepAll);

        var only = Assert.Single(result.Deduped);
        Assert.False(only.IsHq);
    }

    [Fact]
    public void FilterAndGroup_FilterSeesPromotedHqFlag()
    {
        InventoryEntry[] entries =
        [
            Entry(1, InventorySource.Inventory, isHq: false),
            Entry(1, InventorySource.Saddlebag, isHq: true),
            Entry(2, InventorySource.Inventory, isHq: false),
        ];

        var result = InventoryGrouping.FilterAndGroup(entries, entry => entry.IsHq);

        var only = Assert.Single(result.Deduped);
        Assert.Equal(1u, only.ItemId);
        Assert.True(only.IsHq);
    }
}
