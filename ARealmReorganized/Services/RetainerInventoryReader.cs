using System.Collections.Generic;
using ARealmReorganized.Models;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace ARealmReorganized.Services;

internal static unsafe class RetainerInventoryReader
{
    // Retainer bag pages only, five pages of 35 slots each, that's the actual "Inventory"
    // tab in the retainer window. RetainerEquippedItems (gear they're wearing) is excluded so
    // our count matches the game UI and we don't try to move equipped items. RetainerPage6 /
    // RetainerPage7 in the InventoryType enum exist but aren't part of the inventory tab.
    private static readonly InventoryType[] RetainerBags =
    [
        InventoryType.RetainerPage1,
        InventoryType.RetainerPage2,
        InventoryType.RetainerPage3,
        InventoryType.RetainerPage4,
        InventoryType.RetainerPage5,
    ];

    // Reads the *currently active* retainer's bags. The game only keeps the active retainer's
    // inventory hot in memory; for any other retainer we have to rely on the cached snapshot.
    public static IReadOnlyList<InventoryEntry> ReadActive()
    {
        var manager = InventoryManager.Instance();
        if (manager == null) return [];

        var items = new List<InventoryEntry>();
        foreach (var bag in RetainerBags)
        {
            var container = manager->GetInventoryContainer(bag);
            if (container == null) continue;
            for (int slotIndex = 0; slotIndex < container->Size; slotIndex++)
            {
                var slot = container->GetInventorySlot(slotIndex);
                if (slot == null || slot->IsEmpty()) continue;
                items.Add(new InventoryEntry(slot->ItemId, InventorySource.Retainer, slot->IsHighQuality()));
            }
        }
        return items;
    }
}
