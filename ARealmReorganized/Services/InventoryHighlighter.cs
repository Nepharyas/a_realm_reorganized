using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using LuminaItem = Lumina.Excel.Sheets.Item;

namespace ARealmReorganized.Services;

// Draws colored outlines around inventory slots in the game UI to point the player at
// items the plogon thinks are worth acting on. The plogon doesn't move anything itself;
// the outlines are what the player sees so they know which items to grab.
//
// Three colors, one per intent:
//   - Dresser → Armoire (color A): items already in the dresser that can move to the armoire.
//     Shown only inside the dresser addon. (Currently disabled — dresser slot components
//     aren't AtkComponentDragDrops, can't read their iconId via GetIconId without crashing.)
//   - Inventory/Retainer → Armoire (color B): armoire-eligible items found in player bags,
//     armoury chest, saddlebag, and retainer inventories.
//   - Set completion (color C): items that, if moved into the dresser, would complete a
//     partial set already there. Shown anywhere those items currently live (bags, armoury,
//     saddlebag, retainer). Color C wins over B when the same icon matches both — set
//     completion is more specific.
//
// Implementation: we draw rectangles via ImGui's foreground draw list rather than
// mutating the slot's MultiplyRGB. Outlines are independent of the underlying icon's
// color (no cyan-vs-blue render variation from icons influencing the tint), and they
// don't fight the game's own MultiplyRGB writes for greying out unequippable items —
// greyed items keep their grey AND get their highlight outline.
//
// Icons-not-itemIds: detection asks each visible slot for its current icon id via
// AtkComponentDragDrop.GetIconId(), then matches against precomputed icon sets. This
// avoids the ItemOrderModule sorter dance HaselTweaks uses for player bags. Trade-off:
// items sharing an icon (most often NQ vs HQ pairs) will both highlight, which is
// acceptable noise for glamour gear where icons are usually unique.
internal sealed unsafe class InventoryHighlighter
{
    // Player bag grids cover all three layout flavors FFXIV ships (compact / large /
    // expansion). Compact and large reuse the same addon names; expansion uses an "E"
    // suffix. We try every name and skip the ones that aren't visible — that way we
    // don't need to read the player's UI config to know which flavor is active.
    private static readonly string[] PlayerBagGridAddonNames =
    [
        "InventoryGrid0",  "InventoryGrid1",  "InventoryGrid2",  "InventoryGrid3",
        "InventoryGrid0E", "InventoryGrid1E", "InventoryGrid2E", "InventoryGrid3E",
    ];

    // Retainer inventory: compact mode shows a single "RetainerGrid" addon, large mode
    // shows five "RetainerGrid0..4". Same try-all-skip-invisible approach.
    private static readonly string[] RetainerBagGridAddonNames =
    [
        "RetainerGrid",
        "RetainerGrid0", "RetainerGrid1", "RetainerGrid2", "RetainerGrid3", "RetainerGrid4",
    ];

    private const string ArmouryBoardAddonName = "ArmouryBoard";
    private const string SaddlebagAddonName = "InventoryBuddy";

    // Outline visual settings. Thickness is pixels; rounding matches the slight rounding
    // FFXIV slots have so the outline doesn't clip the corners.
    private const float OutlineThickness = 2.5f;
    private const float OutlineCornerRounding = 2f;

    private static readonly Vector4 DresserToArmoireColor = new(0.4f, 1.0f, 0.4f, 1.0f); // green
    private static readonly Vector4 OutsideToArmoireColor = new(0.3f, 0.6f, 1.0f, 1.0f); // blue
    private static readonly Vector4 SetCompletionColor    = new(1.0f, 0.85f, 0.3f, 1.0f); // gold

    private readonly Plugin plugin;
    private readonly HashSet<int> dresserToArmoireIcons = [];
    private readonly HashSet<int> outsideToArmoireIcons = [];
    private readonly HashSet<int> setCompletionIcons = [];

    public InventoryHighlighter(Plugin plugin)
    {
        this.plugin = plugin;
    }

    // Called from MainWindow.RunScan after each scan with the three id sets that drive
    // the highlights. We translate item ids to icon ids once and reuse the icon sets for
    // every per-slot lookup.
    public void SetHighlightSets(
        IEnumerable<uint> dresserToArmoireItemIds,
        IEnumerable<uint> outsideToArmoireItemIds,
        IEnumerable<uint> setCompletionItemIds)
    {
        BuildIconSet(dresserToArmoireIcons, dresserToArmoireItemIds);
        BuildIconSet(outsideToArmoireIcons, outsideToArmoireItemIds);
        BuildIconSet(setCompletionIcons, setCompletionItemIds);
    }

    // Called from the plugin's UiBuilder.Draw hook (i.e. every ImGui frame). The
    // foreground draw list paints on top of game UI, so our outlines stay visible
    // regardless of how the game updates its slots' MultiplyRGB underneath.
    public void OnDraw()
    {
        if (!HasOutsideHighlights()) return;
        var drawList = ImGui.GetForegroundDrawList();
        DrawOutlinesInPlayerBags(drawList);
        DrawOutlinesInArmouryBoard(drawList);
        DrawOutlinesInSaddlebag(drawList);
        DrawOutlinesInRetainer(drawList);
    }

    private bool HasOutsideHighlights() =>
        outsideToArmoireIcons.Count > 0 || setCompletionIcons.Count > 0;

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

    private void DrawOutlinesInPlayerBags(ImDrawListPtr drawList)
    {
        foreach (var addonName in PlayerBagGridAddonNames)
            DrawOutlinesInTypedGridAddon<AddonInventoryGrid>(drawList, addonName, addon => addon->Slots);
    }

    private void DrawOutlinesInRetainer(ImDrawListPtr drawList)
    {
        foreach (var addonName in RetainerBagGridAddonNames)
            DrawOutlinesInTypedGridAddon<AddonInventoryGrid>(drawList, addonName, addon => addon->Slots);
    }

    private void DrawOutlinesInArmouryBoard(ImDrawListPtr drawList) =>
        DrawOutlinesInTypedGridAddon<AddonArmouryBoard>(drawList, ArmouryBoardAddonName, addon => addon->Slots);

    private void DrawOutlinesInSaddlebag(ImDrawListPtr drawList) =>
        DrawOutlinesInTypedGridAddon<AddonInventoryBuddy>(drawList, SaddlebagAddonName, addon => addon->Slots);

    // Generic helper for any addon whose FFXIVClientStructs struct exposes a typed `Slots`
    // span of AtkComponentDragDrop pointers. The slot accessor delegate hands us the span
    // because each addon's struct is a different type.
    private delegate Span<Pointer<AtkComponentDragDrop>> SlotAccessor<T>(T* addon) where T : unmanaged;

    private void DrawOutlinesInTypedGridAddon<T>(
        ImDrawListPtr drawList,
        string addonName,
        SlotAccessor<T> getSlots) where T : unmanaged
    {
        var addonBase = TryGetVisibleAddon(addonName);
        if (addonBase == null) return;
        var addon = (T*)addonBase;
        foreach (var slotPointer in getSlots(addon))
        {
            var slotComponent = slotPointer.Value;
            if (slotComponent == null) continue;
            DrawOutlineForSlot(drawList, slotComponent);
        }
    }

    private void DrawOutlineForSlot(ImDrawListPtr drawList, AtkComponentDragDrop* component)
    {
        var ownerNode = (AtkResNode*)((AtkComponentBase*)component)->OwnerNode;
        if (ownerNode == null || !ownerNode->IsVisible()) return;
        var iconId = component->GetIconId();
        var color = ResolveOutsideColor(iconId);
        if (color is null) return;

        var topLeft = new Vector2(ownerNode->ScreenX, ownerNode->ScreenY);
        var size = new Vector2(ownerNode->Width * ownerNode->ScaleX, ownerNode->Height * ownerNode->ScaleY);
        drawList.AddRect(
            topLeft,
            topLeft + size,
            ImGui.GetColorU32(color.Value),
            OutlineCornerRounding,
            ImDrawFlags.None,
            OutlineThickness);
    }

    private Vector4? ResolveOutsideColor(int iconId)
    {
        if (iconId == 0) return null;
        // Set-completion is more specific (the item finishes a set, not just "could go to
        // the armoire"), so it wins when both match the same icon.
        if (setCompletionIcons.Contains(iconId)) return SetCompletionColor;
        if (outsideToArmoireIcons.Contains(iconId)) return OutsideToArmoireColor;
        return null;
    }

    private static AtkUnitBase* TryGetVisibleAddon(string addonName)
    {
        var wrapper = Service.GameGui.GetAddonByName(addonName, 1);
        if (wrapper.Address == nint.Zero) return null;
        var addon = (AtkUnitBase*)wrapper.Address;
        return addon->IsVisible ? addon : null;
    }
}
