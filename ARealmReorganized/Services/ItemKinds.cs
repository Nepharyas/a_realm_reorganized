using Lumina.Excel.Sheets;

namespace ARealmReorganized.Services;

internal static class ItemKinds
{
    // Anything with an equip slot. Used to keep consumables and materials out of the
    // duplicate report.
    public static bool IsGear(uint itemId)
    {
        var row = Service.DataManager.GetExcelSheet<Item>()?.GetRowOrDefault(itemId);
        if (row is null) return false;
        return row.Value.EquipSlotCategory.RowId != 0;
    }
}
