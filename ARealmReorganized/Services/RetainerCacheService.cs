using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace ARealmReorganized.Services;

internal sealed unsafe class RetainerCacheService
{
    // Standard retainer-inventory addon (35 slots per page).
    private const string AddonInventoryRetainer = "InventoryRetainer";

    // Expanded retainer-inventory addon (35 slots per page, large mode).
    private const string AddonInventoryRetainerLarge = "InventoryRetainerLarge";

    // GameGui.GetAddonByName takes a 1-based addon-instance index; 1 = the first instance,
    // which is what we want for these singleton addons.
    private const int FirstAddonInstance = 1;

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
        IsAddonOpen(AddonInventoryRetainer) || IsAddonOpen(AddonInventoryRetainerLarge);

    private static bool IsAddonOpen(string addonName) =>
        Service.GameGui.GetAddonByName(addonName, FirstAddonInstance) != nint.Zero;

    public void RefreshCacheIfLive()
    {
        var now = DateTime.UtcNow;
        if (now - lastCheck < RefreshInterval) return;
        lastCheck = now;

        var manager = RetainerManager.Instance();
        if (manager == null) return;

        PruneFiredRetainers(manager);

        var activeId = manager->LastSelectedRetainerId;
        if (activeId == 0) return;
        if (!IsRetainerInventoryAddonOpen) return;

        var retainer = manager->GetActiveRetainer();
        if (retainer == null) return;

        var entries = RetainerInventoryReader.ReadActive();
        var newEntries = new List<CachedInventoryEntry>(entries.Count);
        foreach (var entry in entries)
            newEntries.Add(new CachedInventoryEntry { ItemId = entry.ItemId, IsHq = entry.IsHq });

        // If the snapshot is byte-identical to the cached one, just bump the in-memory
        // RefreshedAt (so "X ago" stays accurate in the UI) and skip the disk write.
        if (plugin.Config.CachedRetainers.TryGetValue(activeId, out var existing)
            && EntriesEqual(existing.Entries, newEntries))
        {
            existing.RefreshedAt = now;
            return;
        }

        plugin.Config.CachedRetainers[activeId] = new RetainerInventoryCache
        {
            Name = retainer->NameString,
            RefreshedAt = now,
            Entries = newEntries,
        };
        plugin.Config.Save();
    }

    private void PruneFiredRetainers(RetainerManager* manager)
    {
        if (plugin.Config.CachedRetainers.Count == 0) return;

        var liveIds = new HashSet<ulong>();
        foreach (var retainer in manager->Retainers)
            if (retainer.RetainerId != 0) liveIds.Add(retainer.RetainerId);

        // If RetainerManager hasn't populated yet (e.g. just-logged-in), don't prune — we
        // can't tell apart "no retainers visible right now" from "retainer was fired".
        if (liveIds.Count == 0) return;

        var removed = DictionaryPrune.RemoveKeysWhere(plugin.Config.CachedRetainers, id => !liveIds.Contains(id));
        if (removed > 0) plugin.Config.Save();
    }

    private static bool EntriesEqual(IReadOnlyList<CachedInventoryEntry> a, IReadOnlyList<CachedInventoryEntry> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].ItemId != b[i].ItemId || a[i].IsHq != b[i].IsHq) return false;
        }
        return true;
    }
}
