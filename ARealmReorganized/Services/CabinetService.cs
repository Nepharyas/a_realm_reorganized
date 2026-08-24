using System;
using System.Collections.Generic;
using ARealmReorganized.Logic;
using ARealmReorganized.Models;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace ARealmReorganized.Services;

internal sealed unsafe class CabinetService : ICabinetService
{
    private readonly Plugin plugin;
    private readonly ArmoireEligibility eligibility;
    private readonly DateTime sessionStartedAt = DateTime.UtcNow;

    public CabinetService(Plugin plugin, ArmoireEligibility eligibility)
    {
        this.plugin = plugin;
        this.eligibility = eligibility;
    }

    // True when the snapshot is trustworthy *right now*: either the cabinet UI is loaded (live
    // data) or the cache was refreshed during this plugin session. The persisted cache survives
    // across game sessions but the player can store/remove items between sessions, so a
    // previous-day cache shouldn't be relied on for "is this item already in the armoire".
    public bool IsFresh => IsCabinetLoaded() || plugin.Config.CachedCabinet.RefreshedAt >= sessionStartedAt;

    public bool IsAlreadyStored(uint itemId)
    {
        if (!eligibility.TryGetCabinetId(itemId, out var cabinetId)) return false;
        var ui = UIState.Instance();
        if (ui != null && ui->Cabinet.IsCabinetLoaded()) return ui->Cabinet.IsItemInCabinet(cabinetId);
        return plugin.Config.CachedCabinet.StoredIds.Contains(cabinetId);
    }

    public bool IsStorable(uint itemId) =>
        eligibility.IsEligible(itemId) && !IsAlreadyStored(itemId);

    public IReadOnlyList<uint> ListStorable(IEnumerable<DresserItem> dresserItems)
    {
        var result = new List<uint>();
        var seenItemIds = new HashSet<uint>();
        foreach (var dresserItem in dresserItems)
        {
            if (!IsStorable(dresserItem.ItemId)) continue;
            if (seenItemIds.Add(dresserItem.ItemId)) result.Add(dresserItem.ItemId);
        }
        return result;
    }

    private DateTime lastCheck = DateTime.MinValue;
    private bool loggedFirstLoad;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    public void RefreshCacheIfLive()
    {
        var now = DateTime.UtcNow;
        if (now - lastCheck < RefreshInterval) return;
        lastCheck = now;

        var ui = UIState.Instance();
        if (ui == null || !ui->Cabinet.IsCabinetLoaded()) return;
        var cache = plugin.Config.CachedCabinet;
        cache.StoredIds.Clear();
        foreach (var id in eligibility.AllCabinetIds())
        {
            if (ui->Cabinet.IsItemInCabinet(id)) cache.StoredIds.Add(id);
        }
        cache.RefreshedAt = now;
        plugin.Config.Save();

        if (!loggedFirstLoad)
        {
            Service.Log.Information("Armoire data loaded, {Stored} stored.", cache.StoredIds.Count);
            loggedFirstLoad = true;
        }
    }

    private static bool IsCabinetLoaded()
    {
        var ui = UIState.Instance();
        return ui != null && ui->Cabinet.IsCabinetLoaded();
    }
}
