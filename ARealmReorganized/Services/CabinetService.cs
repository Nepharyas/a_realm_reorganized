using System;
using System.Collections.Generic;
using ARealmReorganized.Logic;
using ARealmReorganized.Models;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace ARealmReorganized.Services;

internal sealed unsafe class CabinetService : ICabinetService
{
    private readonly Plugin plugin;
    private readonly ArmoireEligibility eligibility;

    public CabinetService(Plugin plugin, ArmoireEligibility eligibility)
    {
        this.plugin = plugin;
        this.eligibility = eligibility;
    }

    public bool IsAvailable => IsCabinetLoaded() || plugin.Config.CachedCabinet.RefreshedAt != DateTime.MinValue;

    public bool IsActivatable
    {
        get
        {
            var module = AgentModule.Instance();
            if (module == null) return false;
            var agent = (AgentCabinet*)module->GetAgentByInternalId(AgentId.Cabinet);
            return agent != null && agent->IsActivatable();
        }
    }

    public bool IsAlreadyStored(uint itemId)
    {
        if (!eligibility.TryGetCabinetId(itemId, out var cabinetId)) return false;
        var ui = UIState.Instance();
        if (ui != null && ui->Cabinet.IsCabinetLoaded()) return ui->Cabinet.IsItemInCabinet(cabinetId);
        return plugin.Config.CachedCabinet.StoredIds.Contains(cabinetId);
    }

    public StoreResult Store(uint itemId) => StoreResult.WindowClosed;

    public IReadOnlyList<uint> ListStorable(IEnumerable<DresserItem> dresserItems)
    {
        var result = new List<uint>();
        var seen = new HashSet<uint>();
        foreach (var item in dresserItems)
        {
            if (!eligibility.IsEligible(item.ItemId)) continue;
            if (IsAlreadyStored(item.ItemId)) continue;
            if (seen.Add(item.ItemId)) result.Add(item.ItemId);
        }
        return result;
    }

    private DateTime lastCheck = DateTime.MinValue;
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
    }

    private static bool IsCabinetLoaded()
    {
        var ui = UIState.Instance();
        return ui != null && ui->Cabinet.IsCabinetLoaded();
    }
}
