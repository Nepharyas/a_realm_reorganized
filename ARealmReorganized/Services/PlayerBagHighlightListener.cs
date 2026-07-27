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
// "InventoryLarge" shows two, "InventoryExpansion" all four). The grids are pulled
// from the host's AddonControl child list, which is only safe to walk during a
// PreDraw of that family; walking it from a plain draw hook crashes.
internal sealed unsafe class PlayerBagHighlightListener : AddonHighlightListener
{
    private const string CompactHostName = "Inventory";
    private const string LargeHostName = "InventoryLarge";
    private const string ExpandedHostName = "InventoryExpansion";

    // Exact names only. The hosts own more children than the bag grids (crystal grid,
    // event grids, gil display) and reading one of those as an AddonInventoryGrid is a
    // crash, so nothing looser than a fixed list gets to decide what we cast.
    private static readonly string[] BagGridAddonNames =
    [
        "InventoryGrid",
        "InventoryGrid0",  "InventoryGrid1",  "InventoryGrid2",  "InventoryGrid3",
        "InventoryGrid0E", "InventoryGrid1E", "InventoryGrid2E", "InventoryGrid3E",
    ];

    private static readonly HashSet<string> BagGridNameSet = new(BagGridAddonNames, StringComparer.Ordinal);

    private static readonly string[] HostAddonNames = [CompactHostName, LargeHostName, ExpandedHostName];

    private static readonly string[] ListenedAddonNames = [.. HostAddonNames, .. BagGridAddonNames];

    // Reused between frames to avoid re-allocating; only touched from the main thread.
    private readonly List<(nint Grid, string Name)> bagGrids = [];

    public PlayerBagHighlightListener(InventoryHighlighter highlighter)
        : base(highlighter, ListenedAddonNames)
    {
    }

    // Runs for the host AND for each grid. The grids refresh their slot visuals from the
    // item-order module in their own update, which lands after the host's PreDraw, so a
    // mark written only on the host's pass gets wiped before the grid draws. Re-applying
    // on the grid's own PreDraw is what makes the color stick.
    protected override void ApplyHighlights(AtkUnitBase* addon, string addonName)
    {
        if (!BagGridNameSet.Contains(addonName))
        {
            MarkThroughHost(addon, addonName);
            return;
        }
        var host = FindOpenHost(out var hostName);
        if (host == null) return;
        MarkThroughHost(host, hostName);
    }

    // The layouts keep their host windows around even while closed; the open one is the
    // one actually drawing its root node.
    private static AtkUnitBase* FindOpenHost(out string hostName)
    {
        foreach (var candidateName in HostAddonNames)
        {
            var wrapper = Service.GameGui.GetAddonByName(candidateName, 1);
            if (wrapper.Address == nint.Zero) continue;
            var candidate = (AtkUnitBase*)wrapper.Address;
            if (candidate->RootNode == null || !candidate->RootNode->IsVisible()) continue;
            hostName = candidateName;
            return candidate;
        }
        hostName = "";
        return null;
    }

    private void MarkThroughHost(AtkUnitBase* addon, string addonName)
    {
        var tabIndex = addonName switch
        {
            CompactHostName => ((AddonInventory*)addon)->TabIndex,
            LargeHostName => ((AddonInventoryLarge*)addon)->TabIndex,
            ExpandedHostName => ((AddonInventoryExpansion*)addon)->TabIndex,
            _ => -1,
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
            if (name == null || !BagGridNameSet.Contains(name)) continue;
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
