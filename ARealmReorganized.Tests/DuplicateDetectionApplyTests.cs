using ARealmReorganized.Logic;
using ARealmReorganized.Models;

namespace ARealmReorganized.Tests;

public class DuplicateDetectionApplyTests
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

    private sealed class FakeExecutor : IActionExecutor
    {
        public List<DresserItem> RemovedItems { get; } = [];
        public ActionResult RemoveResult { get; init; } = ActionResult.Success;

        public ActionResult RemoveFromDresser(DresserItem item)
        {
            RemovedItems.Add(item);
            return RemoveResult;
        }

        public ActionResult MoveToArmoire(uint itemId) => ActionResult.Success;
        public ActionResult CompressSet(SetGroup set) => ActionResult.Success;
    }

    // --- dry-run ---

    [Fact]
    public void Apply_DryRun_ExecutorIsStillCalled()
    {
        var result = MakeResult(armoireRedundant: [Item(1, 0)]);
        var executor = new FakeExecutor();

        DuplicateDetection.Apply(result, new HashSet<ushort> { 0 }, willRemove: 1, executor);

        Assert.Single(executor.RemovedItems);
    }

    [Fact]
    public void Apply_DryRun_DuplicatesUpdatedForPreview()
    {
        var result = MakeResult(armoireRedundant: [Item(1, 0), Item(2, 1)]);

        var (returned, _) = DuplicateDetection.Apply(
            result, new HashSet<ushort> { 0 }, willRemove: 1, new FakeExecutor());

        Assert.Equal([Item(2, 1)], returned.ArmoireRedundant);
    }

    [Fact]
    public void Apply_DryRun_RemovedSetPopulatedForPreview()
    {
        var result = MakeResult(armoireRedundant: [Item(1, 0), Item(2, 1)]);

        var (_, removed) = DuplicateDetection.Apply(
            result, new HashSet<ushort> { 0, 1 }, willRemove: 2, new FakeExecutor());

        Assert.Equal(new HashSet<ushort> { 0, 1 }, removed);
    }

    // --- normal mode ---

    [Fact]
    public void Apply_CallsExecutorForEachSelectedItem()
    {
        var items = new[] { Item(1, 0), Item(2, 1) };
        var result = MakeResult(armoireRedundant: items);
        var executor = new FakeExecutor();

        DuplicateDetection.Apply(result, new HashSet<ushort> { 0, 1 }, willRemove: 2, executor);

        Assert.Equal(items, executor.RemovedItems);
    }

    [Fact]
    public void Apply_ReturnedDuplicatesExcludesRemovedSlots()
    {
        var result = MakeResult(armoireRedundant: [Item(1, 0), Item(2, 1)]);

        var (returned, _) = DuplicateDetection.Apply(
            result, new HashSet<ushort> { 0 }, willRemove: 1, new FakeExecutor());

        Assert.Equal([Item(2, 1)], returned.ArmoireRedundant);
    }

    [Fact]
    public void Apply_RemovedSetContainsSuccessfulSlots()
    {
        var result = MakeResult(armoireRedundant: [Item(1, 0), Item(2, 1)]);

        var (_, removed) = DuplicateDetection.Apply(
            result, new HashSet<ushort> { 0, 1 }, willRemove: 2, new FakeExecutor());

        Assert.Equal(new HashSet<ushort> { 0, 1 }, removed);
    }

    [Fact]
    public void Apply_FailedRemovalDoesNotCountAgainstLimit()
    {
        var result = MakeResult(armoireRedundant: [Item(1, 0), Item(2, 1), Item(3, 2)]);
        var executor = new FakeExecutor { RemoveResult = ActionResult.Failed };

        var (returned, removed) = DuplicateDetection.Apply(
            result, new HashSet<ushort> { 0, 1, 2 }, willRemove: 2, executor);

        Assert.Equal(3, executor.RemovedItems.Count);
        Assert.Empty(removed);
        Assert.Same(result, returned);
    }
}
