using ARealmReorganized.Models;
using Lumina.Excel.Sheets;

namespace ARealmReorganized.Logic;

public sealed class ActionExecutor : IActionExecutor
{
    private readonly Plugin plugin;

    public ActionExecutor(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public ActionResult MoveToArmoire(uint itemId)
    {
        var name = ResolveName(itemId);
        if (plugin.Config.DryRun)
        {
            Service.Log.Information($"[dry-run] would move {name} (#{itemId}) to armoire");
            return ActionResult.Success;
        }
        Service.Log.Warning($"Move to armoire not yet implemented for {name} (#{itemId})");
        return ActionResult.Failed;
    }

    public ActionResult CompressSet(SetGroup set)
    {
        if (plugin.Config.DryRun)
        {
            Service.Log.Information(
                $"[dry-run] would compress set '{set.Name}' ({set.Pieces.Count}/{set.TotalPieces} pieces)");
            return ActionResult.Success;
        }
        Service.Log.Warning($"Compress set not yet implemented for '{set.Name}'");
        return ActionResult.Failed;
    }

    public ActionResult RemoveFromDresser(DresserItem item)
    {
        var name = ResolveName(item.ItemId);
        if (plugin.Config.DryRun)
        {
            Service.Log.Information(
                $"[dry-run] would remove {name} (#{item.ItemId}, slot {item.SlotIndex}) from dresser");
            return ActionResult.Success;
        }
        var ok = plugin.Dresser.Remove(item);
        Service.Log.Information(ok
            ? $"Removed {name} (#{item.ItemId}, slot {item.SlotIndex}) from dresser"
            : $"Failed to remove {name} (#{item.ItemId}, slot {item.SlotIndex}) from dresser");
        return ok ? ActionResult.Success : ActionResult.Failed;
    }

    private static string ResolveName(uint itemId)
    {
        var sheet = Service.DataManager.GetExcelSheet<Item>();
        var row = sheet?.GetRowOrDefault(itemId);
        return row?.Name.ExtractText() ?? $"item {itemId}";
    }
}
