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
// v1 note: this draws on ImGui's background draw list (above game UI, below ImGui
// windows) and uses a per-frame rect-overlap check to skip slots covered by other
// visible game addons. That works for the common cases (tooltips, overlapping bag
// addons, dalamud windows) but doesn't actually know z-order. False negatives are
// possible when the source addon is on top of an addon whose bounds happen to
// overlap. The clean fix is to attach custom child nodes to each slot via KamiToolKit
// so the game handles render order, clipping, and lifecycle for us. That's a separate
// refactor planned for a follow-up PR; see the BisBuddy plugin for the reference
// pattern.
//
// Three colors, one per intent:
//   - Dresser → Armoire (color A): items already in the dresser that can move to the
//     armoire. Shown only inside the dresser addon.
//   - Inventory/Retainer → Armoire (color B): armoire-eligible items found in player
//     bags, armoury chest, saddlebag, and retainer inventories.
//   - Set completion (color C): items that, if moved into the dresser, would complete a
//     partial set already there. Shown anywhere those items currently live (bags,
//     armoury, saddlebag, retainer). Color C wins over B when the same icon matches
//     both. Set completion is more specific.
//
// Implementation: we draw rectangles via ImGui's background draw list rather than
// mutating the slot's MultiplyRGB. Outlines are independent of the underlying icon's
// color, don't fight the game's MultiplyRGB writes for greying unequippable items
// (greyed items keep their grey AND get their highlight outline), and won't crash on
// component types that don't share the AtkComponentDragDrop vtable.
//
// Z-order handling: the background draw list paints over all game UI but under any
// ImGui window, so Dalamud windows the player opens (plugin installer, our own main
// window, etc.) naturally cover the outlines. For game-vs-game overlap (e.g. the
// Armoury Chest sitting on top of the Glamour Dresser, or the ItemDetail tooltip
// floating over a slot), we iterate every visible game addon at the start of each
// draw, then skip any outline whose slot rect overlaps another addon's rect. The
// family check below keeps a player-inventory child grid from being masked by its
// own parent host addon.
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
//     confirmed, calling GetIconId crashed). Each slot wraps a 40x40 image node at
//     inner-NodeId 13 whose PartsList → Parts[PartId].UldAsset → AtkTexture.Resource
//     points at the loaded icon resource. That texture resource carries the iconId
//     directly as a struct field, no vtable call needed.
internal sealed unsafe class InventoryHighlighter
{
    // Dresser addon (no typed Slots, we walk by NodeId). Probe v1 verified: 50 slot
    // components at NodeIds 32..81, top-left to bottom-right by row. Probe v2 verified:
    // inside each slot component, the 40x40 item-icon image lives at inner NodeId 13.
    private const string DresserAddonName = "MiragePrismPrismBox";
    private const uint DresserFirstSlotNodeId = 32;
    private const int DresserVisibleSlotCount = 50;
    private const uint DresserSlotIconImageInnerNodeId = 13;

    // NodeType values below this are primitive nodes (image, text, etc.); component
    // nodes (the slot wrappers we care about) start here.
    private const int FirstComponentNodeType = 1000;

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

    // Addon "families": the host inventory addons own their grid addons as children.
    // When we're drawing slots inside a grid addon, the host's bounding box covers all
    // grids inside it, so the overlap check would otherwise mask every grid slot. The
    // grids overlap the host by design, so we exclude same-family pairs from obscuring.
    private static readonly HashSet<string> PlayerInventoryFamily = new(StringComparer.Ordinal)
    {
        "Inventory", "InventoryLarge", "InventoryExpansion",
        "InventoryGrid0",  "InventoryGrid1",  "InventoryGrid2",  "InventoryGrid3",
        "InventoryGrid0E", "InventoryGrid1E", "InventoryGrid2E", "InventoryGrid3E",
    };

    private static readonly HashSet<string> RetainerInventoryFamily = new(StringComparer.Ordinal)
    {
        "InventoryRetainer", "InventoryRetainerLarge",
        "RetainerGrid", "RetainerGrid0", "RetainerGrid1", "RetainerGrid2", "RetainerGrid3", "RetainerGrid4",
    };

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

    private readonly HashSet<int> dresserToArmoireIcons = [];
    private readonly HashSet<int> outsideToArmoireIcons = [];
    private readonly HashSet<int> setCompletionIcons = [];
    private readonly List<AddonRect> visibleAddonRects = [];

    public void SetHighlightSets(
        IEnumerable<uint> dresserToArmoireItemIds,
        IEnumerable<uint> outsideToArmoireItemIds,
        IEnumerable<uint> setCompletionItemIds)
    {
        BuildIconSet(dresserToArmoireIcons, dresserToArmoireItemIds);
        BuildIconSet(outsideToArmoireIcons, outsideToArmoireItemIds);
        BuildIconSet(setCompletionIcons, setCompletionItemIds);
    }

    // Called from the plugin's UiBuilder.Draw hook (every ImGui frame). Background
    // draw list paints between game UI and ImGui windows, so Dalamud windows (our own
    // main window, the plugin installer, etc.) cover our outlines naturally. For game
    // addons that overlap each other, we collect every visible addon's rect up-front
    // and check for overlap per slot.
    public void OnDraw()
    {
        if (!HasAnyHighlights()) return;

        visibleAddonRects.Clear();
        CollectVisibleAddonRects(visibleAddonRects);

        var drawList = ImGui.GetBackgroundDrawList();
        if (dresserToArmoireIcons.Count > 0) DrawOutlinesInDresser(drawList);
        if (HasOutsideHighlights())
        {
            DrawOutlinesInPlayerBags(drawList);
            DrawOutlinesInArmouryBoard(drawList);
            DrawOutlinesInSaddlebag(drawList);
            DrawOutlinesInRetainer(drawList);
        }
    }

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

    private static void CollectVisibleAddonRects(List<AddonRect> destination)
    {
        var manager = RaptureAtkUnitManager.Instance();
        if (manager == null) return;
        ref var allLoaded = ref manager->AllLoadedUnitsList;
        for (var i = 0; i < allLoaded.Count; i++)
        {
            var unit = allLoaded.Entries[i].Value;
            if (unit == null || !unit->IsVisible || unit->RootNode == null) continue;
            var topLeft = new Vector2(unit->X, unit->Y);
            var size = new Vector2(unit->RootNode->Width * unit->Scale, unit->RootNode->Height * unit->Scale);
            destination.Add(new AddonRect(unit->NameString, topLeft, topLeft + size));
        }
    }

    private void DrawOutlinesInDresser(ImDrawListPtr drawList)
    {
        var addonBase = TryGetVisibleAddon(DresserAddonName);
        if (addonBase == null) return;
        var addonScale = addonBase->Scale;
        for (var displaySlotIndex = 0; displaySlotIndex < DresserVisibleSlotCount; displaySlotIndex++)
        {
            var slotNodeId = DresserFirstSlotNodeId + (uint)displaySlotIndex;
            var slotNode = addonBase->GetNodeById(slotNodeId);
            if (slotNode == null || !slotNode->IsVisible()) continue;
            if ((int)slotNode->Type < FirstComponentNodeType) continue;
            var componentNode = (AtkComponentNode*)slotNode;
            if (componentNode->Component == null) continue;

            var iconId = ReadIconIdFromDresserSlot(componentNode->Component);
            if (iconId == 0) continue;
            if (!dresserToArmoireIcons.Contains((int)iconId)) continue;

            DrawHighlightAroundNode(drawList, slotNode, addonScale, DresserToArmoireColor, DresserAddonName);
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

    // Generic helper for any addon whose FFXIVClientStructs struct exposes a typed
    // `Slots` span of AtkComponentDragDrop pointers. The slot accessor delegate hands us
    // the span because each addon's struct is a different type.
    private delegate Span<Pointer<AtkComponentDragDrop>> SlotAccessor<T>(T* addon) where T : unmanaged;

    private void DrawOutlinesInTypedGridAddon<T>(
        ImDrawListPtr drawList,
        string addonName,
        SlotAccessor<T> getSlots) where T : unmanaged
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
            DrawHighlightAroundNode(drawList, ownerNode, addonScale, color.Value, addonName);
        }
    }

    // Dresser slot components aren't AtkComponentDragDrops, so GetIconId would crash.
    // The slot's icon graphic lives in an inner image node (inner NodeId 13); its
    // PartsList → Parts[PartId].UldAsset → AtkTexture.Resource has the iconId as a
    // plain struct field, no vtable indirection.
    private static uint ReadIconIdFromDresserSlot(AtkComponentBase* slotComponent)
    {
        if (slotComponent->UldManager.NodeList == null) return 0;
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
        if (partsList == null || partsList->Parts == null) return 0;
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

    private void DrawHighlightAroundNode(
        ImDrawListPtr drawList, AtkResNode* node, float addonScale, Vector4 color, string sourceAddonName)
    {
        var topLeft = new Vector2(node->ScreenX, node->ScreenY);
        var size = new Vector2(
            node->Width * node->ScaleX * addonScale,
            node->Height * node->ScaleY * addonScale);
        var bottomRight = topLeft + size;
        var outsetVector = new Vector2(OutlineOutset, OutlineOutset);

        if (IsSlotObscured(topLeft - outsetVector, bottomRight + outsetVector, sourceAddonName))
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

    private bool IsSlotObscured(Vector2 slotTopLeft, Vector2 slotBottomRight, string sourceAddonName)
    {
        var sourceFamily = ResolveAddonFamily(sourceAddonName);
        foreach (var addonRect in visibleAddonRects)
        {
            if (addonRect.Name == sourceAddonName) continue;
            // Skip same-family obscurers (host inventory addon over its own child grids).
            if (sourceFamily != null && sourceFamily.Contains(addonRect.Name)) continue;
            if (RectsOverlap(slotTopLeft, slotBottomRight, addonRect.TopLeft, addonRect.BottomRight))
                return true;
        }
        return false;
    }

    private static HashSet<string>? ResolveAddonFamily(string addonName)
    {
        if (PlayerInventoryFamily.Contains(addonName)) return PlayerInventoryFamily;
        if (RetainerInventoryFamily.Contains(addonName)) return RetainerInventoryFamily;
        return null;
    }

    private static bool RectsOverlap(Vector2 aTopLeft, Vector2 aBottomRight, Vector2 bTopLeft, Vector2 bBottomRight) =>
        aTopLeft.X < bBottomRight.X && aBottomRight.X > bTopLeft.X &&
        aTopLeft.Y < bBottomRight.Y && aBottomRight.Y > bTopLeft.Y;

    private static AtkUnitBase* TryGetVisibleAddon(string addonName)
    {
        var wrapper = Service.GameGui.GetAddonByName(addonName, 1);
        if (wrapper.Address == nint.Zero) return null;
        var addon = (AtkUnitBase*)wrapper.Address;
        return addon->IsVisible ? addon : null;
    }

    private readonly record struct AddonRect(string Name, Vector2 TopLeft, Vector2 BottomRight);
}
