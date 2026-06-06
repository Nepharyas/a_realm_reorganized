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
//   - Dresser → Armoire (color A): items already in the dresser that can move to the
//     armoire. Shown only inside the dresser addon.
//   - Inventory/Retainer → Armoire (color B): armoire-eligible items found in player
//     bags, armoury chest, saddlebag, and retainer inventories.
//   - Set completion (color C): items that, if moved into the dresser, would complete a
//     partial set already there. Shown anywhere those items currently live (bags,
//     armoury, saddlebag, retainer). Color C wins over B when the same icon matches
//     both — set completion is more specific.
//
// Implementation: we draw rectangles via ImGui's foreground draw list rather than
// mutating the slot's MultiplyRGB. Outlines are independent of the underlying icon's
// color, don't fight the game's MultiplyRGB writes for greying unequippable items
// (greyed items keep their grey AND get their highlight outline), and won't crash on
// component types that don't share the AtkComponentDragDrop vtable.
//
// Icons-not-itemIds: detection reads each visible slot's current icon id and matches
// against precomputed icon sets. Trade-off: items sharing an icon (most often NQ vs HQ
// pairs) will both highlight, acceptable noise for glamour gear where icons are usually
// unique.
//
// Two icon-id read paths because the addons fall into two families:
//   - Typed `Slots` exposed by FFXIVClientStructs (player bags, armoury, saddlebag,
//     retainer): the slot is an AtkComponentDragDrop, GetIconId() works directly.
//   - Dresser (MiragePrismPrismBox): slot components aren't DragDrops (probed and
//     confirmed — calling GetIconId crashed). Each slot wraps a 40x40 image node at
//     inner-NodeId 13 whose PartsList → Parts[PartId].UldAsset → AtkTexture.Resource
//     points at the loaded icon resource. That texture resource carries the iconId
//     directly as a struct field, no vtable call needed.
internal sealed unsafe class InventoryHighlighter
{
    // Dresser addon (no typed Slots — we walk by NodeId). Probe v1 verified: 50 slot
    // components at NodeIds 32..81, top-left to bottom-right by row. Probe v2 verified:
    // inside each slot component, the 40x40 item-icon image lives at inner NodeId 13.
    private const string DresserAddonName = "MiragePrismPrismBox";
    private const uint DresserFirstSlotNodeId = 32;
    private const int DresserVisibleSlotCount = 50;
    private const uint DresserSlotIconImageInnerNodeId = 13;

    // Player bag grids cover all three layout flavors FFXIV ships (compact / large /
    // expansion). Compact and large reuse the same addon names; expansion uses an "E"
    // suffix. We try every name and skip the ones that aren't visible.
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

    // Outline visual settings. The outline is offset slightly outside the slot so it
    // lands in the dark gap between slots rather than fighting the icon's rarity
    // background (purple/green/gold). Fill adds a wash across the slot so the highlight
    // also reads at a glance from inside the slot, not just at the edge.
    private const float OutlineOutset = 1.5f;
    private const float OutlineThickness = 4.5f;
    private const float OutlineCornerRounding = 3f;
    private const float FillAlpha = 0.35f;

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
    // regardless of how the game updates its slots' MultiplyRGB underneath. The
    // downside is the foreground sits above the game's own tooltip addon too — so we
    // capture the ItemDetail bounds (if it's visible) once per frame and skip drawing
    // outlines that would overlap it, otherwise the outline paints across tooltip text.
    public void OnDraw()
    {
        if (!HasAnyHighlights()) return;
        var drawList = ImGui.GetForegroundDrawList();
        var tooltipBounds = GetItemDetailTooltipBounds();
        if (dresserToArmoireIcons.Count > 0) DrawOutlinesInDresser(drawList, tooltipBounds);
        if (HasOutsideHighlights())
        {
            DrawOutlinesInPlayerBags(drawList, tooltipBounds);
            DrawOutlinesInArmouryBoard(drawList, tooltipBounds);
            DrawOutlinesInSaddlebag(drawList, tooltipBounds);
            DrawOutlinesInRetainer(drawList, tooltipBounds);
        }
    }

    // The item tooltip is its own addon (ItemDetail) which renders above slot icons
    // but below our ImGui foreground draw list. Returns null when there's no tooltip
    // showing, otherwise the tooltip's screen-space bounds.
    private static (Vector2 TopLeft, Vector2 BottomRight)? GetItemDetailTooltipBounds()
    {
        var addon = TryGetVisibleAddon("ItemDetail");
        if (addon == null || addon->RootNode == null) return null;
        var topLeft = new Vector2(addon->X, addon->Y);
        var size = new Vector2(
            addon->RootNode->Width * addon->Scale,
            addon->RootNode->Height * addon->Scale);
        return (topLeft, topLeft + size);
    }

    private static bool RectsOverlap(Vector2 aTopLeft, Vector2 aBottomRight, Vector2 bTopLeft, Vector2 bBottomRight) =>
        aTopLeft.X < bBottomRight.X && aBottomRight.X > bTopLeft.X &&
        aTopLeft.Y < bBottomRight.Y && aBottomRight.Y > bTopLeft.Y;

    private bool HasAnyHighlights() =>
        dresserToArmoireIcons.Count > 0 || HasOutsideHighlights();

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

    private void DrawOutlinesInDresser(ImDrawListPtr drawList, (Vector2, Vector2)? tooltipBounds)
    {
        var addonBase = TryGetVisibleAddon(DresserAddonName);
        if (addonBase == null) return;
        var addonScale = addonBase->Scale;
        for (var displaySlotIndex = 0; displaySlotIndex < DresserVisibleSlotCount; displaySlotIndex++)
        {
            var slotNodeId = DresserFirstSlotNodeId + (uint)displaySlotIndex;
            var slotNode = addonBase->GetNodeById(slotNodeId);
            if (slotNode == null || !slotNode->IsVisible()) continue;
            if ((int)slotNode->Type < 1000) continue;
            var componentNode = (AtkComponentNode*)slotNode;
            if (componentNode->Component == null) continue;

            var iconId = ReadIconIdFromDresserSlot(componentNode->Component);
            if (iconId == 0) continue;
            if (!dresserToArmoireIcons.Contains((int)iconId)) continue;

            DrawHighlightAroundNode(drawList, slotNode, addonScale, DresserToArmoireColor, tooltipBounds);
        }
    }

    private void DrawOutlinesInPlayerBags(ImDrawListPtr drawList, (Vector2, Vector2)? tooltipBounds)
    {
        foreach (var addonName in PlayerBagGridAddonNames)
            DrawOutlinesInTypedGridAddon<AddonInventoryGrid>(drawList, addonName, addon => addon->Slots, tooltipBounds);
    }

    private void DrawOutlinesInRetainer(ImDrawListPtr drawList, (Vector2, Vector2)? tooltipBounds)
    {
        foreach (var addonName in RetainerBagGridAddonNames)
            DrawOutlinesInTypedGridAddon<AddonInventoryGrid>(drawList, addonName, addon => addon->Slots, tooltipBounds);
    }

    private void DrawOutlinesInArmouryBoard(ImDrawListPtr drawList, (Vector2, Vector2)? tooltipBounds) =>
        DrawOutlinesInTypedGridAddon<AddonArmouryBoard>(drawList, ArmouryBoardAddonName, addon => addon->Slots, tooltipBounds);

    private void DrawOutlinesInSaddlebag(ImDrawListPtr drawList, (Vector2, Vector2)? tooltipBounds) =>
        DrawOutlinesInTypedGridAddon<AddonInventoryBuddy>(drawList, SaddlebagAddonName, addon => addon->Slots, tooltipBounds);

    // Generic helper for any addon whose FFXIVClientStructs struct exposes a typed
    // `Slots` span of AtkComponentDragDrop pointers. The slot accessor delegate hands us
    // the span because each addon's struct is a different type.
    private delegate Span<Pointer<AtkComponentDragDrop>> SlotAccessor<T>(T* addon) where T : unmanaged;

    private void DrawOutlinesInTypedGridAddon<T>(
        ImDrawListPtr drawList,
        string addonName,
        SlotAccessor<T> getSlots,
        (Vector2, Vector2)? tooltipBounds) where T : unmanaged
    {
        var addonBase = TryGetVisibleAddon(addonName);
        if (addonBase == null) return;
        var addon = (T*)addonBase;
        // The addon has an overall scale (set by the player's UI options); slot
        // dimensions we read from the node are in addon-local units, so we have to
        // multiply by this to get the actual on-screen size. Position fields (ScreenX/Y)
        // already account for the cumulative transforms.
        var addonScale = addonBase->Scale;
        foreach (var slotPointer in getSlots(addon))
        {
            var slotComponent = slotPointer.Value;
            if (slotComponent == null) continue;
            var ownerNode = (AtkResNode*)((AtkComponentBase*)slotComponent)->OwnerNode;
            if (ownerNode == null || !ownerNode->IsVisible()) continue;
            var iconId = slotComponent->GetIconId();
            var color = ResolveOutsideColor(iconId);
            if (color is null) continue;
            DrawHighlightAroundNode(drawList, ownerNode, addonScale, color.Value, tooltipBounds);
        }
    }

    // Dresser slot components aren't AtkComponentDragDrops, so GetIconId would crash.
    // The slot's icon graphic lives in an inner image node (inner NodeId 13); its
    // PartsList → Parts[PartId].UldAsset → AtkTexture.Resource has the iconId as a
    // plain struct field, no vtable indirection.
    private static uint ReadIconIdFromDresserSlot(AtkComponentBase* slotComponent)
    {
        for (var i = 0; i < slotComponent->UldManager.NodeListCount; i++)
        {
            var inner = slotComponent->UldManager.NodeList[i];
            if (inner == null) continue;
            if (inner->NodeId != DresserSlotIconImageInnerNodeId) continue;
            if (inner->Type != NodeType.Image) continue;
            return ReadIconIdFromImageNode((AtkImageNode*)inner);
        }
        return 0;
    }

    private static uint ReadIconIdFromImageNode(AtkImageNode* imageNode)
    {
        var partsList = imageNode->PartsList;
        if (partsList == null) return 0;
        if (imageNode->PartId >= partsList->PartCount) return 0;
        var part = &partsList->Parts[imageNode->PartId];
        if (part->UldAsset == null) return 0;
        var texture = &part->UldAsset->AtkTexture;
        if (texture->TextureType != TextureType.Resource) return 0;
        if (texture->Resource == null) return 0;
        return texture->Resource->IconId;
    }

    private Vector4? ResolveOutsideColor(int iconId)
    {
        if (iconId == 0) return null;
        // Set-completion is more specific (the item finishes a set, not just "could go
        // to the armoire"), so it wins when both match the same icon.
        if (setCompletionIcons.Contains(iconId)) return SetCompletionColor;
        if (outsideToArmoireIcons.Contains(iconId)) return OutsideToArmoireColor;
        return null;
    }

    private static void DrawHighlightAroundNode(
        ImDrawListPtr drawList, AtkResNode* node, float addonScale, Vector4 color,
        (Vector2 TopLeft, Vector2 BottomRight)? tooltipBounds)
    {
        var topLeft = new Vector2(node->ScreenX, node->ScreenY);
        var size = new Vector2(
            node->Width * node->ScaleX * addonScale,
            node->Height * node->ScaleY * addonScale);
        var bottomRight = topLeft + size;
        var outsetVector = new Vector2(OutlineOutset, OutlineOutset);

        // If the game's item tooltip is showing over this slot's area, skip the
        // outline — we draw on the ImGui foreground which paints above the tooltip,
        // and an outline on top of tooltip text just makes the text harder to read.
        if (tooltipBounds is { } tooltip &&
            RectsOverlap(topLeft - outsetVector, bottomRight + outsetVector, tooltip.TopLeft, tooltip.BottomRight))
            return;

        var fillColor = new Vector4(color.X, color.Y, color.Z, FillAlpha);

        drawList.AddRectFilled(topLeft, bottomRight, ImGui.GetColorU32(fillColor), OutlineCornerRounding);
        drawList.AddRect(
            topLeft - outsetVector,
            bottomRight + outsetVector,
            ImGui.GetColorU32(color),
            OutlineCornerRounding + OutlineOutset,
            ImDrawFlags.None,
            OutlineThickness);
    }

    private static AtkUnitBase* TryGetVisibleAddon(string addonName)
    {
        var wrapper = Service.GameGui.GetAddonByName(addonName, 1);
        if (wrapper.Address == nint.Zero) return null;
        var addon = (AtkUnitBase*)wrapper.Address;
        return addon->IsVisible ? addon : null;
    }
}
