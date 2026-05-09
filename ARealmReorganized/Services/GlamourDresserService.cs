using System;
using System.Collections.Generic;
using ARealmReorganized.Models;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace ARealmReorganized.Services;

internal sealed unsafe class GlamourDresserService : IGlamourDresserService
{
    private readonly Plugin plugin;

    public GlamourDresserService(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public IReadOnlyList<DresserItem> Snapshot()
    {
        if (HasLiveData()) return ReadLive();
        return ReadFromCache();
    }

    private DateTime lastCheck = DateTime.MinValue;
    private bool loggedFirstLoad;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    public void RefreshCacheIfLive()
    {
        var now = DateTime.UtcNow;
        if (now - lastCheck < RefreshInterval) return;
        lastCheck = now;

        if (!HasLiveData()) return;
        var live = ReadLive();
        var cache = plugin.Config.CachedDresser;
        cache.Slots.Clear();
        foreach (var di in live)
        {
            cache.Slots.Add(new CachedDresserSlot
            {
                Slot = di.SlotIndex,
                ItemId = di.ItemId,
                Stain0 = di.Stain0,
                Stain1 = di.Stain1,
            });
        }
        cache.RefreshedAt = now;
        plugin.Config.Save();

        if (!loggedFirstLoad)
        {
            plugin.LogBuffer.Add($"Dresser data loaded ({live.Count} items)");
            loggedFirstLoad = true;
        }
    }

    private static bool HasLiveData()
    {
        var manager = MirageManager.Instance();
        if (manager == null) return false;
        var ids = manager->PrismBoxItemIds;
        for (int i = 0; i < ids.Length; i++)
            if (ids[i] != 0) return true;
        return false;
    }

    private static IReadOnlyList<DresserItem> ReadLive()
    {
        var manager = MirageManager.Instance();
        if (manager == null) return Array.Empty<DresserItem>();
        var ids = manager->PrismBoxItemIds;
        var s0 = manager->PrismBoxStain0Ids;
        var s1 = manager->PrismBoxStain1Ids;
        var result = new List<DresserItem>();
        for (int i = 0; i < ids.Length; i++)
        {
            if (ids[i] == 0) continue;
            result.Add(new DresserItem(ids[i], (ushort)i, s0[i], s1[i]));
        }
        return result;
    }

    private IReadOnlyList<DresserItem> ReadFromCache()
    {
        var slots = plugin.Config.CachedDresser.Slots;
        var result = new List<DresserItem>(slots.Count);
        foreach (var s in slots)
        {
            if (s.ItemId == 0) continue;
            result.Add(new DresserItem(s.ItemId, s.Slot, s.Stain0, s.Stain1));
        }
        return result;
    }
}
