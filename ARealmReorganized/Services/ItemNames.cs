using Lumina.Excel.Sheets;

namespace ARealmReorganized.Services;

internal static class ItemNames
{
    public static string Resolve(uint itemId)
    {
        var sheet = Service.DataManager.GetExcelSheet<Item>();
        var row = sheet?.GetRowOrDefault(itemId);
        if (row is not null) return row.Value.Name.ExtractText();
        Service.Log.Warning($"Item name lookup failed for id {itemId}");
        return $"Item #{itemId}";
    }
}
