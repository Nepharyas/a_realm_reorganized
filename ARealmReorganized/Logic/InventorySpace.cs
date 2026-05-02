using FFXIVClientStructs.FFXIV.Client.Game;

namespace ARealmReorganized.Logic;

public static unsafe class InventorySpace
{
    private const uint GlamourPrismItemId = 21800;

    private static readonly InventoryType[] MainBags =
    {
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
    };

    public static int FreeSlots()
    {
        var inv = InventoryManager.Instance();
        if (inv == null) return 0;
        var free = 0;
        foreach (var type in MainBags)
        {
            var container = inv->GetInventoryContainer(type);
            if (container == null) continue;
            for (int i = 0; i < container->Size; i++)
            {
                if (container->GetInventorySlot(i)->ItemId == 0) free++;
            }
        }
        return free;
    }

    public static int GlamourPrismCount()
    {
        var inv = InventoryManager.Instance();
        if (inv == null) return 0;
        return inv->GetInventoryItemCount(GlamourPrismItemId);
    }
}
