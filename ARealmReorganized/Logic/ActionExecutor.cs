using ARealmReorganized.Models;
using ARealmReorganized.Services;

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
        var name = ItemNames.Resolve(itemId);
        if (plugin.Config.DryRun)
        {
            plugin.LogBuffer.DryRun($"[dry-run] would move {name} (#{itemId}) to armoire");
            return ActionResult.Success;
        }
        plugin.LogBuffer.Warn($"Move to armoire not yet implemented for {name} (#{itemId})");
        return ActionResult.Failed;
    }

    public ActionResult CompressSet(SetGroup set)
    {
        if (plugin.Config.DryRun)
        {
            plugin.LogBuffer.DryRun($"[dry-run] would compress set '{set.Name}' ({set.Pieces.Count}/{set.TotalPieces} pieces)");
            return ActionResult.Success;
        }
        plugin.LogBuffer.Warn($"Compress set not yet implemented for '{set.Name}'");
        return ActionResult.Failed;
    }

    public ActionResult MoveFromRetainer(uint itemId, ulong retainerId)
    {
        var name = ItemNames.Resolve(itemId);
        if (plugin.Config.DryRun)
        {
            plugin.LogBuffer.DryRun($"[dry-run] would pull {name} (#{itemId}) from retainer {retainerId} into inventory");
            return ActionResult.Success;
        }
        plugin.LogBuffer.Warn($"Move from retainer not yet implemented for {name} (#{itemId})");
        return ActionResult.Failed;
    }

    public ActionResult RemoveFromDresser(DresserItem item)
    {
        var name = ItemNames.Resolve(item.ItemId);
        if (plugin.Config.DryRun)
        {
            plugin.LogBuffer.DryRun($"[dry-run] would remove {name} (#{item.ItemId}, slot {item.SlotIndex}) from dresser");
            return ActionResult.Success;
        }
        var ok = plugin.Dresser.Remove(item);
        plugin.LogBuffer.Info(ok
            ? $"Removed {name} (#{item.ItemId}, slot {item.SlotIndex}) from dresser"
            : $"Failed to remove {name} (#{item.ItemId}, slot {item.SlotIndex}) from dresser");
        return ok ? ActionResult.Success : ActionResult.Failed;
    }

}
