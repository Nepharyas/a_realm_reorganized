using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using LuminaItem = Lumina.Excel.Sheets.Item;

namespace ARealmReorganized.Services;

// Outlines inventory slots so the player can see which items the plogon flagged. green =
// in the dresser, can move to armoire. blue = in armoury/saddlebag/retainer, can move to
// armoire. gold = would complete a partial dresser set if put in the dresser (gold wins
// over blue on the same icon).
//
// Matching is by icon id, not item id, so NQ/HQ pairs sharing an icon both light up.
// Fine for glam gear where icons are unique enough.
//
// The player's own bags aren't covered here yet. Unlike the other windows, their slot
// components don't carry the item icon (bags render through the item-order module), so
// finding what's in each slot means walking that module's sorter every frame, which is
// fiddly and crash-prone from a plain draw hook. That's left for the KamiToolKit pass,
// which hooks per-addon lifecycle events and can do it safely (see BisBuddy).
//
// v1 draws on the imgui background draw list. it sits above game UI but below imgui
// windows, so our own windows cover the outlines. there's no game-side occlusion though,
// so an outline can paint over a game tooltip or a window dragged on top of the slots.
// the proper fix is attaching child nodes per slot via KamiToolKit so the game clips
// them for us; that's a later pass (see BisBuddy for the pattern).
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

    // Retainer inventory: compact mode shows a single "RetainerGrid" addon, large mode
    // shows five "RetainerGrid0..4". We try every name and skip whichever aren't on screen.
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

    // The legend in the main window draws swatches in these same colors, so they're
    // internal rather than private.
    internal static readonly Vector4 DresserToArmoireColor = new(0.4f, 1.0f, 0.4f, 1.0f); // green
    internal static readonly Vector4 OutsideToArmoireColor = new(0.3f, 0.6f, 1.0f, 1.0f); // blue
    internal static readonly Vector4 SetCompletionColor    = new(1.0f, 0.85f, 0.3f, 1.0f); // gold

    private readonly HashSet<int> dresserToArmoireIcons = [];
    private readonly HashSet<int> outsideToArmoireIcons = [];
    private readonly HashSet<int> setCompletionIcons = [];

    public void SetHighlightSets(
        IEnumerable<uint> dresserToArmoireItemIds,
        IEnumerable<uint> outsideToArmoireItemIds,
        IEnumerable<uint> setCompletionItemIds)
    {
        BuildIconSet(dresserToArmoireIcons, dresserToArmoireItemIds);
        BuildIconSet(outsideToArmoireIcons, outsideToArmoireItemIds);
        BuildIconSet(setCompletionIcons, setCompletionItemIds);
    }

    // Called from the plugin's UiBuilder.Draw hook every ImGui frame.
    public void OnDraw()
    {
        if (!HasAnyHighlights()) return;

        var drawList = ImGui.GetBackgroundDrawList();
        if (dresserToArmoireIcons.Count > 0) DrawOutlinesInDresser(drawList);
        if (HasOutsideHighlights())
        {
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

            DrawHighlightAroundNode(drawList, slotNode, addonScale, DresserToArmoireColor);
        }
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
            DrawHighlightAroundNode(drawList, ownerNode, addonScale, color.Value);
        }
    }

    // Dresser slot components aren't AtkComponentDragDrops, so GetIconId would crash.
    // The slot's icon graphic lives in an inner image node (inner NodeId 13); its
    // PartsList -> Parts[PartId].UldAsset -> AtkTexture.Resource has the iconId as a
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

    private static void DrawHighlightAroundNode(
        ImDrawListPtr drawList, AtkResNode* node, float addonScale, Vector4 color)
    {
        var topLeft = new Vector2(node->ScreenX, node->ScreenY);
        var size = new Vector2(
            node->Width * node->ScaleX * addonScale,
            node->Height * node->ScaleY * addonScale);
        var bottomRight = topLeft + size;
        var outsetVector = new Vector2(OutlineOutset, OutlineOutset);

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

    // We check the root node's visibility rather than the addon's IsVisible flag: the
    // inventory/retainer grids are child addons whose own IsVisible stays false even when
    // they're on screen, but their root node tracks whether they're actually shown.
    private static AtkUnitBase* TryGetVisibleAddon(string addonName)
    {
        var wrapper = Service.GameGui.GetAddonByName(addonName, 1);
        if (wrapper.Address == nint.Zero) return null;
        var addon = (AtkUnitBase*)wrapper.Address;
        return addon->RootNode != null && addon->RootNode->IsVisible() ? addon : null;
    }
}
