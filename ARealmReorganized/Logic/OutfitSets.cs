using Lumina.Excel.Sheets;

namespace ARealmReorganized.Logic;

// The MirageStoreSetItem sheet: one row per outfit, keyed by the outfit's own item id,
// naming the piece for each of the 11 gear slots (0 where the outfit has none). An
// outfit stored in the dresser takes a single prism box slot under that item id.
public static class OutfitSets
{
    public const int SlotCount = 11;

    // Which pieces a stored outfit actually holds. Since 7.4 the two stain bytes of an
    // outfit entry are a bitmask over the 11 slots, low byte first, with a set bit meaning
    // the slot is still empty.
    public static bool IsPieceStored(byte stain0, byte stain1, int slot)
    {
        var emptySlots = stain0 | (stain1 << 8);
        return ((emptySlots >> slot) & 1) == 0;
    }

    public static bool TryGetPieceIds(uint setItemId, out uint[] pieceIds)
    {
        pieceIds = [];
        var setSheet = Service.DataManager.GetExcelSheet<MirageStoreSetItem>();
        var setRow = setSheet?.GetRowOrDefault(setItemId);
        if (setRow is null) return false;
        pieceIds = PieceIds(setRow.Value);
        return true;
    }

    // In column order; a 0 means the outfit has no piece for that slot.
    public static uint[] PieceIds(MirageStoreSetItem setRow) =>
    [
        setRow.MainHand.RowId, setRow.OffHand.RowId, setRow.Head.RowId,
        setRow.Body.RowId, setRow.Hands.RowId, setRow.Legs.RowId,
        setRow.Feet.RowId, setRow.Earrings.RowId, setRow.Necklace.RowId,
        setRow.Bracelets.RowId, setRow.Ring.RowId,
    ];
}
