using Lumina.Excel.Sheets;

namespace ARealmReorganized.Services;

internal static class ItemNames
{
    public static string Resolve(uint itemId)
    {
        var sheet = Service.DataManager.GetExcelSheet<Item>();
        var row = sheet?.GetRowOrDefault(itemId);
        return row?.Name.ExtractText() ?? $"Item #{itemId}";
    }
}
