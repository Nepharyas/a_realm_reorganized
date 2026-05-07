using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace ARealmReorganized.Services;

internal sealed unsafe class RetainerCacheService
{
    private readonly Plugin plugin;
    private DateTime lastCheck = DateTime.MinValue;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    public RetainerCacheService(Plugin plugin)
    {
        this.plugin = plugin;
    }

    // 0 unless the retainer-inventory addon is currently open. RetainerManager.LastSelectedRetainerId
    // sticks at its previous value after the bell closes, so we gate on the addon being live.
    public ulong ActiveRetainerId
    {
        get
        {
            if (!IsRetainerInventoryAddonOpen) return 0UL;
            var manager = RetainerManager.Instance();
            return manager == null ? 0UL : manager->LastSelectedRetainerId;
        }
    }

    public bool IsRetainerInventoryAddonOpen =>
        Service.GameGui.GetAddonByName("InventoryRetainer", 1) != IntPtr.Zero
        || Service.GameGui.GetAddonByName("InventoryRetainerLarge", 1) != IntPtr.Zero;

    public void RefreshCacheIfLive()
    {
        var now = DateTime.UtcNow;
        if (now - lastCheck < RefreshInterval) return;
        lastCheck = now;

        var manager = RetainerManager.Instance();
        if (manager == null) return;

        var activeId = manager->LastSelectedRetainerId;
        if (activeId == 0) return;
        if (!IsRetainerInventoryAddonOpen) return;

        var retainer = manager->GetActiveRetainer();
        if (retainer == null) return;

        var entries = RetainerInventoryReader.ReadActive();
        var snapshot = new RetainerInventoryCache
        {
            Name = retainer->NameString,
            RefreshedAt = now,
            Entries = new List<CachedInventoryEntry>(entries.Count),
        };
        foreach (var entry in entries)
            snapshot.Entries.Add(new CachedInventoryEntry { ItemId = entry.ItemId, IsHq = entry.IsHq });

        plugin.Config.CachedRetainers[activeId] = snapshot;
        plugin.Config.Save();
    }
}
