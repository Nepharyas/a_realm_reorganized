using ARealmReorganized.Models;
using Lumina.Excel.Sheets;

namespace ARealmReorganized.Logic;

public sealed class DryRunExecutor : IActionExecutor
{
    public ActionResult MoveToArmoire(uint itemId)
    {
        var name = ResolveName(itemId);
        Service.Log.Information($"[dry-run] would move {name} (#{itemId}) to armoire");
        return ActionResult.Success;
    }

    public ActionResult CompressSet(SetGroup set)
    {
        Service.Log.Information(
            $"[dry-run] would compress set '{set.Name}' ({set.Pieces.Count}/{set.TotalPieces} pieces)");
        return ActionResult.Success;
    }

    private static string ResolveName(uint itemId)
    {
        var sheet = Service.DataManager.GetExcelSheet<Item>();
        var row = sheet?.GetRowOrDefault(itemId);
        return row?.Name.ExtractText() ?? $"item {itemId}";
    }
}
