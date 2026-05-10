using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using LuminaItem = Lumina.Excel.Sheets.Item;

namespace ARealmReorganized.Services;

// Tints inventory slots in the game UI to point the player at items the plogon thinks are
// worth acting on. The plogon doesn't move anything itself; this highlighter is what the
// player sees so they know which items to grab.
//
// Three colors, one per intent:
//   - Dresser → Armoire (color A): items already in the dresser that can move to the armoire.
//     Shown only inside the dresser addon.
//   - Inventory/Retainer → Armoire (color B): armoire-eligible items found in player bags,
//     armoury chest, saddlebag, and retainer inventories.
//   - Set completion (color C): items that, if moved into the dresser, would complete a
//     partial set already there. Shown anywhere those items currently live (bags, armoury,
//     saddlebag, retainer). Color C wins over B when the same icon matches both — set
//     completion is more specific.
//
// Icons-not-itemIds: detection asks each visible slot for its current icon id via
// AtkComponentDragDrop.GetIconId(), then matches against precomputed icon sets. This
// sidesteps the dresser's pagination + sort + filter mapping (no public state for the
// active page), and avoids the ItemOrderModule sorter dance HaselTweaks uses for player
// bags. Trade-off: items sharing an icon (most often NQ vs HQ pairs) will both highlight,
// which is acceptable noise for glamour gear where icons are usually unique.
internal sealed unsafe class InventoryHighlighter
{
    // The dresser addon (MiragePrismPrismBox) isn't typed in FFXIVClientStructs, so its
    // 50-slot grid is reached via raw NodeIds. Probe verified: slot components are 50
    // AtkComponentDragDrops at NodeIds 32..81 in display order (top-left → bottom-right
    // by row), 5 rows of 10. NodeIds stay stable across pages/sort/filter changes; only
    // the icons inside them update.
    private const string DresserAddonName = "MiragePrismPrismBox";
    private const uint DresserFirstSlotNodeId = 32;
    private const int DresserVisibleSlotCount = 50;

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

    // MultiplyRed/Green/Blue are bytes interpreted as percentages where 100 is the game's
    // neutral baseline (no change). Below 100 dims that channel; above 100 actively
    // brightens it. HaselTweaks's dim-everything feature only uses ≤100 so they never
    // need to brighten — but tinting needs the brighten side too, so the target channel
    // pushes well past 100 while the others get knocked down. These are first-pass
    // values; tune in-game for taste.
    private const byte NeutralBrightness = 100;
    private static readonly SlotTint Neutral = new(NeutralBrightness, NeutralBrightness, NeutralBrightness);
    private static readonly SlotTint DresserToArmoireTint  = new( 50, 220,  50); // green
    private static readonly SlotTint OutsideToArmoireTint  = new( 50, 130, 220); // blue
    private static readonly SlotTint SetCompletionTint     = new(230, 200,  50); // gold

    private readonly Plugin plugin;
    private readonly HashSet<int> dresserToArmoireIcons = [];
    private readonly HashSet<int> outsideToArmoireIcons = [];
    private readonly HashSet<int> setCompletionIcons = [];
    private DateTime lastTickAt = DateTime.MinValue;
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

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

    public void Tick()
    {
        var now = DateTime.UtcNow;
        if (now - lastTickAt < TickInterval) return;
        lastTickAt = now;

        // Dresser walk is disabled: NodeIds 32..81 in MiragePrismPrismBox aren't
        // AtkComponentDragDrops despite the matching inner-node count from the probe;
        // calling GetIconId() on them crashes the game. Need to identify the actual
        // component type and find a safe icon-id read path before re-enabling.
        if (HasOutsideHighlights())
        {
            HighlightInPlayerBags();
            HighlightInArmouryBoard();
            HighlightInSaddlebag();
            HighlightInRetainer();
        }
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

    private void HighlightInDresser()
    {
        var addon = TryGetVisibleAddon(DresserAddonName);
        if (addon == null) return;

        for (var displaySlotIndex = 0; displaySlotIndex < DresserVisibleSlotCount; displaySlotIndex++)
        {
            var slotNodeId = DresserFirstSlotNodeId + (uint)displaySlotIndex;
            var slotNode = addon->GetNodeById(slotNodeId);
            if (slotNode == null) continue;
            ApplyTintToComponentNode(slotNode, ResolveDresserTint);
        }
    }

    private void HighlightInPlayerBags()
    {
        foreach (var addonName in PlayerBagGridAddonNames)
            HighlightTypedGridAddon<AddonInventoryGrid>(addonName, ResolveOutsideTint, addon => addon->Slots);
    }

    private void HighlightInRetainer()
    {
        foreach (var addonName in RetainerBagGridAddonNames)
            HighlightTypedGridAddon<AddonInventoryGrid>(addonName, ResolveOutsideTint, addon => addon->Slots);
    }

    private void HighlightInArmouryBoard() =>
        HighlightTypedGridAddon<AddonArmouryBoard>(
            ArmouryBoardAddonName, ResolveOutsideTint, addon => addon->Slots);

    private void HighlightInSaddlebag() =>
        HighlightTypedGridAddon<AddonInventoryBuddy>(
            SaddlebagAddonName, ResolveOutsideTint, addon => addon->Slots);

    // Generic helper for any addon whose FFXIVClientStructs struct exposes a typed `Slots`
    // span of AtkComponentDragDrop pointers. The slot accessor delegate hands us the span
    // because each addon's struct is a different type.
    private delegate Span<Pointer<AtkComponentDragDrop>> SlotAccessor<T>(T* addon) where T : unmanaged;

    private void HighlightTypedGridAddon<T>(
        string addonName,
        Func<int, SlotTint> resolveTint,
        SlotAccessor<T> getSlots) where T : unmanaged
    {
        var addonBase = TryGetVisibleAddon(addonName);
        if (addonBase == null) return;
        var addon = (T*)addonBase;
        foreach (var slotPointer in getSlots(addon))
        {
            var slotComponent = slotPointer.Value;
            if (slotComponent == null) continue;
            ApplyTintToSlotComponent(slotComponent, resolveTint);
        }
    }

    private SlotTint ResolveDresserTint(int iconId)
    {
        if (iconId == 0) return Neutral;
        return dresserToArmoireIcons.Contains(iconId) ? DresserToArmoireTint : Neutral;
    }

    private SlotTint ResolveOutsideTint(int iconId)
    {
        if (iconId == 0) return Neutral;
        // Set-completion is more specific (the item finishes a set, not just "could go to
        // the armoire"), so it wins when both match the same icon.
        if (setCompletionIcons.Contains(iconId)) return SetCompletionTint;
        if (outsideToArmoireIcons.Contains(iconId)) return OutsideToArmoireTint;
        return Neutral;
    }

    // For nodes reached by NodeId (only the dresser at the moment): the node IS an
    // AtkComponentNode; its inner Component is the AtkComponentDragDrop.
    private static void ApplyTintToComponentNode(AtkResNode* node, Func<int, SlotTint> resolveTint)
    {
        if ((int)node->Type < 1000) return;
        var componentNode = (AtkComponentNode*)node;
        var component = (AtkComponentDragDrop*)componentNode->Component;
        if (component == null) return;
        var iconId = component->GetIconId();
        WriteTint(node, resolveTint(iconId));
    }

    // For typed addons we already have the AtkComponentDragDrop pointer; we tint its
    // OwnerNode (which is the AtkComponentNode wrapping it).
    private static void ApplyTintToSlotComponent(AtkComponentDragDrop* component, Func<int, SlotTint> resolveTint)
    {
        var ownerNode = (AtkResNode*)((AtkComponentBase*)component)->OwnerNode;
        if (ownerNode == null) return;
        var iconId = component->GetIconId();
        WriteTint(ownerNode, resolveTint(iconId));
    }

    private static void WriteTint(AtkResNode* node, SlotTint tint)
    {
        node->MultiplyRed = tint.Red;
        node->MultiplyGreen = tint.Green;
        node->MultiplyBlue = tint.Blue;
    }

    private static AtkUnitBase* TryGetVisibleAddon(string addonName)
    {
        var wrapper = Service.GameGui.GetAddonByName(addonName, 1);
        if (wrapper.Address == nint.Zero) return null;
        var addon = (AtkUnitBase*)wrapper.Address;
        return addon->IsVisible ? addon : null;
    }

    private readonly record struct SlotTint(byte Red, byte Green, byte Blue);
}
