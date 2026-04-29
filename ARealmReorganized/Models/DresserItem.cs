namespace ARealmReorganized.Models;

public readonly record struct DresserItem(
    uint ItemId,
    ushort SlotIndex,
    byte Stain0,
    byte Stain1);
