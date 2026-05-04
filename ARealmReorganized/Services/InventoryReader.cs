using System.Collections.Generic;
using ARealmReorganized.Models;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace ARealmReorganized.Services;

internal static unsafe class InventoryReader
{
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

    public static IReadOnlyList<InventoryEntry> ReadAll()
    {
        var manager = InventoryManager.Instance();
        if (manager == null) return [];

        var items = new List<InventoryEntry>();
        AppendItemsFrom(manager, MainInventoryBags, InventorySource.Inventory, items);
        AppendItemsFrom(manager, ArmouryBags, InventorySource.Armoury, items);
        AppendItemsFrom(manager, SaddlebagBags, InventorySource.Saddlebag, items);
        return items;
    }

    private static void AppendItemsFrom(
        InventoryManager* manager,
        InventoryType[] bags,
        InventorySource source,
        List<InventoryEntry> output)
    {
        foreach (var bag in bags)
        {
            var container = manager->GetInventoryContainer(bag);
            if (container == null) continue;
            for (int slotIndex = 0; slotIndex < container->Size; slotIndex++)
            {
                var slot = container->GetInventorySlot(slotIndex);
                if (slot == null || slot->IsEmpty()) continue;
                output.Add(new InventoryEntry(slot->ItemId, source, slot->IsHighQuality()));
            }
        }
    }
}
