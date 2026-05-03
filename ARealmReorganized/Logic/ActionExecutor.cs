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
            Log($"[dry-run] would move {name} (#{itemId}) to armoire");
            return ActionResult.Success;
        }
        LogWarn($"Move to armoire not yet implemented for {name} (#{itemId})");
        return ActionResult.Failed;
    }

    public ActionResult CompressSet(SetGroup set)
    {
        if (plugin.Config.DryRun)
        {
            Log($"[dry-run] would compress set '{set.Name}' ({set.Pieces.Count}/{set.TotalPieces} pieces)");
            return ActionResult.Success;
        }
        LogWarn($"Compress set not yet implemented for '{set.Name}'");
        return ActionResult.Failed;
    }

    public ActionResult RemoveFromDresser(DresserItem item)
    {
        var name = ResolveName(item.ItemId);
        if (plugin.Config.DryRun)
        {
            Log($"[dry-run] would remove {name} (#{item.ItemId}, slot {item.SlotIndex}) from dresser");
            return ActionResult.Success;
        }
        var ok = plugin.Dresser.Remove(item);
        Log(ok
            ? $"Removed {name} (#{item.ItemId}, slot {item.SlotIndex}) from dresser"
            : $"Failed to remove {name} (#{item.ItemId}, slot {item.SlotIndex}) from dresser");
        return ok ? ActionResult.Success : ActionResult.Failed;
    }

    private void Log(string msg)
    {
        Service.Log.Information(msg);
        plugin.LogBuffer.Add(msg);
    }

    private void LogWarn(string msg)
    {
        Service.Log.Warning(msg);
        plugin.LogBuffer.Add(msg);
    }

    private static string ResolveName(uint itemId)
    {
        var sheet = Service.DataManager.GetExcelSheet<Item>();
        var row = sheet?.GetRowOrDefault(itemId);
        return row?.Name.ExtractText() ?? $"item {itemId}";
    }
}
