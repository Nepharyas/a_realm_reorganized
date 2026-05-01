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
}
