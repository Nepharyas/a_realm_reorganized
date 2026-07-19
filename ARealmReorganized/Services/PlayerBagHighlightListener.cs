using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace ARealmReorganized.Services;

// The player's own bags. Their slot components don't carry the item icon (the game
// renders bags through the item-order module), so we can't read slots like the other
// windows. Instead, the module's sorter lists the items in display order: the entry at
// display index N tells us which container slot the item really lives in, and the
// on-screen grids show the sorted order in consecutive 35-slot pages. So we line up
// each visible grid slot with its sorter entry and color from the item found there.
//
// Which grids are on screen depends on the inventory layout the player uses. Each
// layout is a different host window (compact "Inventory" pages one grid via tabs,
// "InventoryLarge" shows two, "InventoryExpansion" all four), so we hook the hosts'
// PreDraw and pull their child grid addons from AddonControl. That's only safe from
// the host's own PreDraw, when the child list is stable; walking it from a plain
// draw hook crashes.
internal sealed unsafe class PlayerBagHighlightListener : AddonHighlightListener
{
    private const string CompactHostName = "Inventory";
    private const string LargeHostName = "InventoryLarge";
    private const string ExpandedHostName = "InventoryExpansion";
    private const string BagGridNamePrefix = "InventoryGrid";

    // Grids get registered too: not for drawing (ApplyHighlights skips them), but so
    // their PreFinalize clears our marks before their nodes are freed. Host and grids
    // can tear down in any order.
    private static readonly string[] ListenedAddonNames =
    [
        CompactHostName, LargeHostName, ExpandedHostName,
        "InventoryGrid",
        "InventoryGrid0",  "InventoryGrid1",  "InventoryGrid2",  "InventoryGrid3",
        "InventoryGrid0E", "InventoryGrid1E", "InventoryGrid2E", "InventoryGrid3E",
    ];

    // Reused between frames to avoid re-allocating; only touched from the main thread.
    private readonly List<(nint Grid, string Name)> bagGrids = [];

    public PlayerBagHighlightListener(InventoryHighlighter highlighter)
        : base(highlighter, ListenedAddonNames)
    {
    }

    protected override void ApplyHighlights(AtkUnitBase* addon, string addonName)
    {
        var tabIndex = addonName switch
        {
            CompactHostName => ((AddonInventory*)addon)->TabIndex,
            LargeHostName => ((AddonInventoryLarge*)addon)->TabIndex,
            ExpandedHostName => ((AddonInventoryExpansion*)addon)->TabIndex,
            _ => -1, // a grid addon; the host's pass handles it
        };
        if (tabIndex < 0) return;

        var orderModule = ItemOrderModule.Instance();
        var sorter = orderModule == null ? null : orderModule->InventorySorter;
        var inventory = InventoryManager.Instance();
        if (sorter == null || inventory == null) return;
        if (sorter->SortFunctionIndex != -1) return; // a sort is mid-run, positions aren't settled
        if (sorter->ItemsPerPage <= 0) return;

        CollectBagGrids(addon, addonName);
        if (bagGrids.Count == 0) return;

        // Each grid shows one 35-slot page of the sorted order; the host's tab picks
        // which run of pages is on screen.
        var displayIndex = tabIndex * bagGrids.Count * sorter->ItemsPerPage;
        foreach (var (gridPointer, _) in bagGrids)
        {
            var grid = (AddonInventoryGrid*)gridPointer;
            foreach (var slotPointer in grid->Slots)
            {
                var slotItemId = ReadItemIdAtDisplayIndex(sorter, inventory, displayIndex);
                displayIndex++;

                var slotComponent = slotPointer.Value;
                if (slotComponent == null) continue;
                var ownerNode = (AtkResNode*)((AtkComponentBase*)slotComponent)->OwnerNode;
                if (ownerNode == null) continue;
                SetNodeColor(ownerNode, Highlighter.ResolveOutsideColorByItemId(slotItemId));
            }
        }
    }

    // The host owns its grids as child addons; sorting their names ordinally puts them
    // in display order ("InventoryGrid0E" .. "InventoryGrid3E").
    private void CollectBagGrids(AtkUnitBase* hostAddon, string hostName)
    {
        bagGrids.Clear();
        var control = hostName switch
        {
            CompactHostName => &((AddonInventory*)hostAddon)->AddonControl,
            LargeHostName => &((AddonInventoryLarge*)hostAddon)->AddonControl,
            _ => &((AddonInventoryExpansion*)hostAddon)->AddonControl,
        };

        // While the host is still linking its children (right after opening), the list
        // isn't safe to walk yet; skip the frame and pick the grids up on the next one.
        if (!control->IsChildSetupComplete) return;

        foreach (var childInfoPointer in control->ChildAddons)
        {
            var childInfo = childInfoPointer.Value;
            if (childInfo == null || childInfo->AtkUnitBase == null) continue;
            var name = childInfo->AtkUnitBase->NameString;
            if (name == null || !name.StartsWith(BagGridNamePrefix, StringComparison.Ordinal)) continue;
            bagGrids.Add(((nint)childInfo->AtkUnitBase, name));
        }
        bagGrids.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
    }

    private static uint ReadItemIdAtDisplayIndex(
        ItemOrderModuleSorter* sorter, InventoryManager* inventory, int displayIndex)
    {
        if (displayIndex < 0 || displayIndex >= sorter->Items.Count) return 0;
        var entry = sorter->Items[displayIndex].Value;
        if (entry == null) return 0;
        var container = inventory->GetInventoryContainer(sorter->InventoryType + entry->Page);
        if (container == null) return 0;
        var slot = container->GetInventorySlot(entry->Slot);
        if (slot == null) return 0;
        return slot->ItemId;
    }
}
