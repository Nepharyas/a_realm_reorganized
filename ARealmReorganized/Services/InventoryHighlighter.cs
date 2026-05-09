using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace ARealmReorganized.Services;

// Highlights inventory slots whose item is eligible for the armoire (or other targets).
//
// Current implementation: detects which player-inventory addons are open and which slots
// contain eligible items, but does NOT yet mutate node colors. The node-walk per addon
// type (compact / expansion / large; player / retainer / dresser) needs to be done
// against a live game session to confirm the right field paths — adding it blind risks
// corrupting the addon UI. The detection layer lives here so that wiring is in one place
// once the per-addon node access is verified.
internal sealed unsafe class InventoryHighlighter
{
    private readonly Plugin plugin;
    private readonly HashSet<uint> armoireEligibleIds = new();
    private DateTime lastTick = DateTime.MinValue;
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

    private static readonly (string AddonName, InventoryType Bag)[] PlayerInventoryAddons =
    [
        ("InventoryGrid0E", InventoryType.Inventory1),
        ("InventoryGrid1E", InventoryType.Inventory2),
        ("InventoryGrid2E", InventoryType.Inventory3),
        ("InventoryGrid3E", InventoryType.Inventory4),
    ];

    public InventoryHighlighter(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void SetArmoireEligible(IEnumerable<uint> itemIds)
    {
        armoireEligibleIds.Clear();
        foreach (var id in itemIds) armoireEligibleIds.Add(id);
    }

    public void Tick()
    {
        if (armoireEligibleIds.Count == 0) return;

        var now = DateTime.UtcNow;
        if (now - lastTick < TickInterval) return;
        lastTick = now;

        var manager = InventoryManager.Instance();
        if (manager == null) return;

        foreach (var (addonName, bag) in PlayerInventoryAddons)
        {
            var wrapper = Service.GameGui.GetAddonByName(addonName, 1);
            if (wrapper.Address == nint.Zero) continue;
            var addon = (AtkUnitBase*)wrapper.Address;
            if (!addon->IsVisible) continue;

            // TODO(runtime): walk the addon's slot component nodes here and apply
            // MultiplyRed/Green/Blue to tint slots whose itemId is in armoireEligibleIds.
            // The right access path is `AddonInventoryExpansion.GridChildInfo` (per
            // FFXIVClientStructs), but the exact slot-component layout needs verification
            // against a live game state before we mutate nodes. Until then, this loop
            // logs detection only.
        }
    }
}
