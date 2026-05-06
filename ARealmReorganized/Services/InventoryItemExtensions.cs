using FFXIVClientStructs.FFXIV.Client.Game;

namespace ARealmReorganized.Services;

internal static class InventoryItemExtensions
{
    public static bool IsEmpty(this in InventoryItem item) => item.ItemId == 0;
}
