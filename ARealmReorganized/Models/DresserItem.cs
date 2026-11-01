namespace ARealmReorganized.Models;

// A piece sitting in the dresser. Pieces folded into an outfit share the outfit's slot
// and carry the outfit's item id; the dresser keeps no dye for those, so they read as
// undyed.
public readonly record struct DresserItem(
    uint ItemId,
    ushort SlotIndex,
    byte Stain0,
    byte Stain1,
    uint OutfitItemId = 0)
{
    public bool IsInOutfit => OutfitItemId != 0;
}
