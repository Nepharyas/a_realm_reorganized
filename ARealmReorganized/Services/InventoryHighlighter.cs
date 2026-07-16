using System;
using System.Collections.Generic;
using System.Numerics;
using LuminaItem = Lumina.Excel.Sheets.Item;

namespace ARealmReorganized.Services;

// Coordinates the in-game slot highlights. Holds the id sets each scan produces and
// answers "what color should this slot be" for the per-window listeners that do the
// actual tinting (see AddonHighlightListener). green = in the dresser, can move to
// armoire. blue = in bags/armoury/saddlebag/retainer, can move to armoire. gold =
// would complete a partial dresser set if put in the dresser (gold wins over blue on
// the same icon).
//
// Matching is by icon id, not item id, so NQ/HQ pairs sharing an icon both light up.
// Fine for glam gear where icons are unique enough.
internal sealed class InventoryHighlighter : IDisposable
{
    // The legend in the main window draws swatches in these same colors, so they're
    // internal rather than private.
    internal static readonly Vector4 DresserToArmoireColor = new(0.4f, 1.0f, 0.4f, 1.0f); // green
    internal static readonly Vector4 OutsideToArmoireColor = new(0.3f, 0.6f, 1.0f, 1.0f); // blue
    internal static readonly Vector4 SetCompletionColor    = new(1.0f, 0.85f, 0.3f, 1.0f); // gold

    private readonly HashSet<int> dresserToArmoireIcons = [];
    private readonly HashSet<int> outsideToArmoireIcons = [];
    private readonly HashSet<int> setCompletionIcons = [];
    private readonly List<AddonHighlightListener> listeners;

    public InventoryHighlighter()
    {
        listeners =
        [
            new DresserHighlightListener(this),
            new ArmouryHighlightListener(this),
            new SaddlebagHighlightListener(this),
            new RetainerHighlightListener(this),
            new PlayerBagHighlightListener(this),
        ];
    }

    public void Dispose()
    {
        foreach (var listener in listeners) listener.Dispose();
    }

    public void SetHighlightSets(
        IEnumerable<uint> dresserToArmoireItemIds,
        IEnumerable<uint> outsideToArmoireItemIds,
        IEnumerable<uint> setCompletionItemIds)
    {
        BuildIconSet(dresserToArmoireIcons, dresserToArmoireItemIds);
        BuildIconSet(outsideToArmoireIcons, outsideToArmoireItemIds);
        BuildIconSet(setCompletionIcons, setCompletionItemIds);
    }

    internal Vector4? ResolveDresserColor(int iconId)
    {
        if (iconId == 0) return null;
        return dresserToArmoireIcons.Contains(iconId) ? DresserToArmoireColor : null;
    }

    internal Vector4? ResolveOutsideColor(int iconId)
    {
        if (iconId == 0) return null;
        // Set-completion is more specific (the item finishes a set, not just "could go
        // to the armoire"), so it wins when both match the same icon.
        if (setCompletionIcons.Contains(iconId)) return SetCompletionColor;
        if (outsideToArmoireIcons.Contains(iconId)) return OutsideToArmoireColor;
        return null;
    }

    // For the player bags, where detection hands us the item rather than the slot's
    // icon; goes through the item's icon so the matching stays consistent everywhere.
    internal Vector4? ResolveOutsideColorByItemId(uint itemId)
    {
        if (itemId == 0) return null;
        var row = Service.DataManager.GetExcelSheet<LuminaItem>()?.GetRowOrDefault(itemId);
        if (row is null) return null;
        return ResolveOutsideColor((int)row.Value.Icon);
    }

    private static void BuildIconSet(HashSet<int> destination, IEnumerable<uint> itemIds)
    {
        destination.Clear();
        var itemSheet = Service.DataManager.GetExcelSheet<LuminaItem>();
        if (itemSheet is null) return;
        foreach (var itemId in itemIds)
        {
            var row = itemSheet.GetRowOrDefault(itemId);
            if (row is null) continue;
            var iconId = (int)row.Value.Icon;
            if (iconId != 0) destination.Add(iconId);
        }
    }
}
