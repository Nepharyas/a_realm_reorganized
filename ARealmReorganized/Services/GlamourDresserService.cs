using System;
using System.Collections.Generic;
using ARealmReorganized.Logic;
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

    public bool IsLive => HasLiveData();

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

        var slots = new List<CachedDresserSlot>(live.Count);
        foreach (var dresserItem in live)
        {
            slots.Add(new CachedDresserSlot
            {
                Slot = dresserItem.SlotIndex,
                ItemId = dresserItem.ItemId,
                Stain0 = dresserItem.Stain0,
                Stain1 = dresserItem.Stain1,
                OutfitItemId = dresserItem.OutfitItemId,
            });
        }

        // The game keeps the prism box in memory until you change zone, so this runs long
        // after the dresser window is shut. Only touch the config when something actually
        // moved, otherwise every tick rewrites the whole file for nothing.
        var cache = plugin.Config.CachedDresser;
        cache.RefreshedAt = now;
        if (!SlotsEqual(cache.Slots, slots))
        {
            cache.Slots = slots;
            plugin.Config.Save();
        }

        if (!loggedFirstLoad)
        {
            Service.Log.Information("Dresser data loaded, {Items} items.", live.Count);
            loggedFirstLoad = true;
        }
    }

    private static bool SlotsEqual(List<CachedDresserSlot> a, List<CachedDresserSlot> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i].Slot != b[i].Slot || a[i].ItemId != b[i].ItemId
                || a[i].Stain0 != b[i].Stain0 || a[i].Stain1 != b[i].Stain1
                || a[i].OutfitItemId != b[i].OutfitItemId) return false;
        }
        return true;
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
            // An outfit sits in one slot under its own item id; what matters to us is
            // the pieces inside it.
            if (OutfitSets.TryGetPieceIds(ids[i], out var pieceIds))
            {
                AddStoredPieces(result, ids[i], (ushort)i, pieceIds, s0[i], s1[i]);
                continue;
            }
            result.Add(new DresserItem(ids[i], (ushort)i, s0[i], s1[i]));
        }
        return result;
    }

    private static void AddStoredPieces(
        List<DresserItem> result, uint outfitItemId, ushort slotIndex, uint[] pieceIds, byte stain0, byte stain1)
    {
        for (var slot = 0; slot < pieceIds.Length; slot++)
        {
            if (pieceIds[slot] == 0) continue;
            if (!OutfitSets.IsPieceStored(stain0, stain1, slot)) continue;
            result.Add(new DresserItem(pieceIds[slot], slotIndex, 0, 0, outfitItemId));
        }
    }

    private IReadOnlyList<DresserItem> ReadFromCache()
    {
        var slots = plugin.Config.CachedDresser.Slots;
        var result = new List<DresserItem>(slots.Count);
        foreach (var s in slots)
        {
            if (s.ItemId == 0) continue;
            result.Add(new DresserItem(s.ItemId, s.Slot, s.Stain0, s.Stain1, s.OutfitItemId));
        }
        return result;
    }
}
