using System.Collections.Generic;
using ARealmReorganized.Models;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace ARealmReorganized.Services;

internal static unsafe class InventoryReader
{
    // What a scan found, plus whether the saddlebag was actually readable. The game only
    // keeps saddlebag contents loaded while you can reach it, so in an instance (or before
    // you have opened it) its containers report empty rather than missing. Without the
    // flag a scan run in there would quietly claim you own nothing in the saddlebag.
    public readonly record struct Result(
        IReadOnlyList<InventoryEntry> Entries,
        bool SaddlebagAvailable);

    private static readonly InventoryType[] MainInventoryBags =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
    ];

    private static readonly InventoryType[] ArmouryBags =
    [
        InventoryType.ArmoryMainHand,
        InventoryType.ArmoryOffHand,
        InventoryType.ArmoryHead,
        InventoryType.ArmoryBody,
        InventoryType.ArmoryHands,
        InventoryType.ArmoryLegs,
        InventoryType.ArmoryFeets,
        InventoryType.ArmoryEar,
        InventoryType.ArmoryNeck,
        InventoryType.ArmoryWrist,
        InventoryType.ArmoryRings,
    ];

    private static readonly InventoryType[] SaddlebagBags =
    [
        InventoryType.SaddleBag1,
        InventoryType.SaddleBag2,
    ];

    // Only there for players on a subscription, so these never gate the availability flag.
    private static readonly InventoryType[] PremiumSaddlebagBags =
    [
        InventoryType.PremiumSaddleBag1,
        InventoryType.PremiumSaddleBag2,
    ];

    public static Result ReadAll()
    {
        var manager = InventoryManager.Instance();
        if (manager == null) return new Result([], false);

        var items = new List<InventoryEntry>();
        AppendItemsFrom(manager, MainInventoryBags, InventorySource.Inventory, items);
        AppendItemsFrom(manager, ArmouryBags, InventorySource.Armoury, items);
        var saddlebagAvailable = AppendItemsFrom(manager, SaddlebagBags, InventorySource.Saddlebag, items);
        AppendItemsFrom(manager, PremiumSaddlebagBags, InventorySource.Saddlebag, items);
        return new Result(items, saddlebagAvailable);
    }

    // Returns false when any of the containers isn't loaded, meaning what we read from it
    // is absence of data rather than absence of items.
    private static bool AppendItemsFrom(
        InventoryManager* manager,
        InventoryType[] bags,
        InventorySource source,
        List<InventoryEntry> output)
    {
        var allLoaded = true;
        foreach (var bag in bags)
        {
            var container = manager->GetInventoryContainer(bag);
            if (container == null || !container->IsLoaded)
            {
                allLoaded = false;
                continue;
            }
            for (int slotIndex = 0; slotIndex < container->Size; slotIndex++)
            {
                var slot = container->GetInventorySlot(slotIndex);
                if (slot == null || slot->IsEmpty()) continue;
                output.Add(new InventoryEntry(slot->ItemId, source, slot->IsHighQuality()));
            }
        }
        return allLoaded;
    }
}
