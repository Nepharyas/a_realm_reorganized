using ARealmReorganized.Models;

namespace ARealmReorganized.Logic;

public enum ActionResult
{
    Success,
    Skipped,
    Failed,
}

public interface IActionExecutor
{
    ActionResult MoveToArmoire(uint itemId);
    ActionResult CompressSet(SetGroup set);
    ActionResult RemoveFromDresser(DresserItem item);
}
