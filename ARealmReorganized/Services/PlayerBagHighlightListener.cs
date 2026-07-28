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
// on-screen grids show the sorted order in consecutive 35-slot pages. Each grid marks
// its own page on its own PreDraw; anything written earlier gets wiped by the grid's
// update before it draws.
//
// Which grids are on screen depends on the inventory layout the player uses. Each
// layout is a different host window (compact "Inventory" pages one grid via tabs,
// "InventoryLarge" shows two, "InventoryExpansion" all four). The grids are child
// addons of the host; that list is only safe to walk once the host reports its
// children linked, and it doesn't change while the host is open, so it's collected
// once per host session and dropped when a grid finalizes.
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

    // The open host and its grids in display order, cached for the host session.
    private nint cachedHost;
    private string cachedHostName = "";
    private readonly List<nint> bagGrids = [];

    public PlayerBagHighlightListener(InventoryHighlighter highlighter)
        : base(highlighter, BagGridAddonNames)
    {
    }

    protected override void ApplyHighlights(AtkUnitBase* addon, string addonName)
    {
        var gridIndex = GridDisplayIndex((nint)addon);
        if (gridIndex < 0) return;

        var orderModule = ItemOrderModule.Instance();
        var sorter = orderModule == null ? null : orderModule->InventorySorter;
        var inventory = InventoryManager.Instance();
        if (sorter == null || inventory == null) return;
        if (sorter->SortFunctionIndex != -1) return; // a sort is mid-run, positions aren't settled
        if (sorter->ItemsPerPage <= 0) return;

        var tabIndex = ReadTabIndex((AtkUnitBase*)cachedHost, cachedHostName);
        var displayIndex = (tabIndex * bagGrids.Count + gridIndex) * sorter->ItemsPerPage;

        foreach (var slotPointer in ((AddonInventoryGrid*)addon)->Slots)
        {
            var slotItemId = ReadItemIdAtDisplayIndex(sorter, inventory, displayIndex);
            displayIndex++;
            SetSlotColor((AtkComponentBase*)slotPointer.Value, Highlighter.ResolveOutsideColorByItemId(slotItemId));
        }
    }

    // Position of this grid in the host's display order. When the drawing grid isn't in
    // the cached list (say the layout changed under us), the cache gets rebuilt once.
    private int GridDisplayIndex(nint gridAddress)
    {
        var index = bagGrids.IndexOf(gridAddress);
        if (index >= 0) return index;
        ClearGridCache();
        if (!TryFillGridCache()) return -1;
        return bagGrids.IndexOf(gridAddress);
    }

    protected override void OnMarksCleared() => ClearGridCache();

    private void ClearGridCache()
    {
        cachedHost = 0;
        cachedHostName = "";
        bagGrids.Clear();
    }

    private bool TryFillGridCache()
    {
        var host = FindOpenHost(out var hostName);
        if (host == null) return false;
        var control = GetAddonControl(host, hostName);
        // While the host is still linking its children (right after opening), the list
        // isn't safe to walk yet; try again on a later frame.
        if (!control->IsChildSetupComplete) return false;

        var grids = new List<(nint Address, string Name)>();
        foreach (var childInfoPointer in control->ChildAddons)
        {
            var childInfo = childInfoPointer.Value;
            if (childInfo == null || childInfo->AtkUnitBase == null) continue;
            var name = childInfo->AtkUnitBase->NameString;
            if (name == null || !BagGridNameSet.Contains(name)) continue;
            grids.Add(((nint)childInfo->AtkUnitBase, name));
        }
        if (grids.Count == 0) return false;

        // Ordinal name order is display order ("InventoryGrid0E" .. "InventoryGrid3E").
        grids.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        foreach (var (address, _) in grids) bagGrids.Add(address);
        cachedHost = (nint)host;
        cachedHostName = hostName;
        return true;
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

    private static int ReadTabIndex(AtkUnitBase* host, string hostName) => hostName switch
    {
        CompactHostName => ((AddonInventory*)host)->TabIndex,
        LargeHostName => ((AddonInventoryLarge*)host)->TabIndex,
        _ => ((AddonInventoryExpansion*)host)->TabIndex,
    };

    private static AtkAddonControl* GetAddonControl(AtkUnitBase* host, string hostName) => hostName switch
    {
        CompactHostName => &((AddonInventory*)host)->AddonControl,
        LargeHostName => &((AddonInventoryLarge*)host)->AddonControl,
        _ => &((AddonInventoryExpansion*)host)->AddonControl,
    };

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
