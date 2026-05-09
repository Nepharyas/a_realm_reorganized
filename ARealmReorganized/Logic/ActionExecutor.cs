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
            plugin.LogBuffer.DryRun($"[dry-run] would move {name} to armoire");
            return ActionResult.Success;
        }
        plugin.LogBuffer.Warn($"Move to armoire not yet implemented for {name}");
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
        var retainerName = ResolveRetainerName(retainerId);
        if (plugin.Config.DryRun)
        {
            plugin.LogBuffer.DryRun($"[dry-run] would pull {name} from retainer {retainerName} into inventory");
            return ActionResult.Success;
        }
        plugin.LogBuffer.Warn($"Move from retainer not yet implemented for {name} (retainer {retainerName})");
        return ActionResult.Failed;
    }

    public ActionResult RemoveFromDresser(DresserItem item)
    {
        var name = ItemNames.Resolve(item.ItemId);
        if (plugin.Config.DryRun)
        {
            plugin.LogBuffer.DryRun($"[dry-run] would remove {name} from dresser");
            return ActionResult.Success;
        }
        var ok = plugin.Dresser.Remove(item);
        plugin.LogBuffer.Info(ok
            ? $"Removed {name} from dresser"
            : $"Failed to remove {name} from dresser");
        return ok ? ActionResult.Success : ActionResult.Failed;
    }

    private string ResolveRetainerName(ulong retainerId)
    {
        if (plugin.Config.CachedRetainers.TryGetValue(retainerId, out var snap)
            && !string.IsNullOrEmpty(snap.Name))
            return snap.Name;
        return $"#{retainerId}";
    }
}
