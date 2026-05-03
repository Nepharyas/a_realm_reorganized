namespace ARealmReorganized.Models;

public readonly record struct InventoryEntry(
    uint ItemId,
    InventorySource Source);
