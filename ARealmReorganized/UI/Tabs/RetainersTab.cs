using System;
using System.Collections.Generic;
using System.Numerics;
using ARealmReorganized.Logic;
using ARealmReorganized.Models;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace ARealmReorganized.UI.Tabs;

internal sealed class RetainersTab
{
    private readonly Plugin plugin;
    private readonly MainWindow main;

    public RetainersTab(Plugin plugin, MainWindow main)
    {
        this.plugin = plugin;
        this.main = main;
    }

    public string TabLabel => $"Retainers → Armoire ({CountEligibleAcrossRetainers()})###retainers";

    public void Draw()
    {
        main.DrawCabinetUnavailableBanner();

        var cached = plugin.Config.CachedRetainers;
        if (cached.Count == 0)
        {
            MainWindow.TextDisabledWrapped(
                "No retainer data yet. Open each retainer's inventory once at the summoning bell to populate this list.");
            return;
        }

        using var list = ImRaii.Child("##retainerlist", Vector2.Zero);
        if (list)
        {
            var now = DateTime.UtcNow;
            var activeRetainerId = plugin.Retainers.ActiveRetainerId;
            foreach (var (retainerId, snap) in cached)
                DrawRetainerSection(retainerId, snap, now, retainerId == activeRetainerId);
        }
    }

    private int CountEligibleAcrossRetainers()
    {
        var total = 0;
        foreach (var snap in plugin.Config.CachedRetainers.Values)
            total += GroupRetainerSnapshot(snap).Deduped.Count;
        return total;
    }

    private InventoryGrouping.Result GroupRetainerSnapshot(RetainerInventoryCache snap)
    {
        var entries = new List<InventoryEntry>(snap.Entries.Count);
        foreach (var cached in snap.Entries)
            entries.Add(new InventoryEntry(cached.ItemId, InventorySource.Retainer, cached.IsHq));
        return InventoryGrouping.FilterAndGroup(entries, e => plugin.Cabinet.IsStorable(e.ItemId));
    }

    private void DrawRetainerSection(ulong retainerId, RetainerInventoryCache snap, DateTime now, bool isActive)
    {
        var grouped = GroupRetainerSnapshot(snap);
        if (grouped.Deduped.Count == 0) return;

        var displayName = string.IsNullOrEmpty(snap.Name) ? $"Retainer #{retainerId}" : snap.Name;
        var refreshedAgo = snap.RefreshedAt == DateTime.MinValue ? "?" : MainWindow.Humanize(now - snap.RefreshedAt);
        var activeMarker = isActive ? " [active]" : "";
        var status = $"{grouped.Deduped.Count} eligible, refreshed {refreshedAgo} ago";
        var headerLabel = $"{displayName}{activeMarker} ({status})###retainer{retainerId}";
        if (!ImGui.CollapsingHeader(headerLabel, ImGuiTreeNodeFlags.DefaultOpen)) return;

        foreach (var entry in grouped.Deduped)
            main.DrawItemRow(entry);
    }
}
