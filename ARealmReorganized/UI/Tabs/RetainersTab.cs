using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ARealmReorganized.Logic;
using ARealmReorganized.Models;
using ARealmReorganized.Services;
using Dalamud.Bindings.ImGui;

namespace ARealmReorganized.UI.Tabs;

internal sealed class RetainersTab
{
    private readonly Plugin plugin;
    private readonly MainWindow main;
    private readonly Dictionary<ulong, HashSet<uint>> selectedByRetainer = new();
    // Items the most recent dry-run Step 1 "would have" moved into inventory. Lets dry-run
    // Step 2 simulate the next stage even though no items actually changed bags.
    private readonly HashSet<uint> dryRunPendingArmoireMoves = new();

    public RetainersTab(Plugin plugin, MainWindow main)
    {
        this.plugin = plugin;
        this.main = main;
    }

    public string TabLabel => $"Sort from retainers ({CountEligibleAcrossRetainers()})###retainers";

    public void Reset() => dryRunPendingArmoireMoves.Clear();

    public void OnDryRunToggled() => dryRunPendingArmoireMoves.Clear();

    public void Draw()
    {
        main.DrawCabinetUnavailableBanner();

        var cached = plugin.Config.CachedRetainers;
        PruneSelectionsForMissingRetainers();

        if (cached.Count == 0)
        {
            MainWindow.TextDisabledWrapped(
                "No retainer data yet. Open each retainer's inventory once at the summoning bell to populate this list.");
            return;
        }

        ImGui.PushTextWrapPos();
        ImGui.TextColored(UiColors.Info,
            "Summoning bells aren't always next to an Armoire, so this happens in two steps. " +
            "Step 1 moves your selected retainer items into your inventory (limited by free inventory slots). " +
            "Step 2 then moves those items from inventory into the Armoire. You can run them back-to-back or pause between.");
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        DrawActionBar();
        ImGui.Separator();

        if (ImGui.BeginChild("##retainerlist", Vector2.Zero))
        {
            var now = DateTime.UtcNow;
            var activeRetainerId = plugin.Retainers.ActiveRetainerId;
            foreach (var (retainerId, snap) in cached)
                DrawRetainerSection(retainerId, snap, now, retainerId == activeRetainerId);
        }
        ImGui.EndChild();
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

    private void PruneSelectionsForMissingRetainers()
    {
        if (selectedByRetainer.Count == 0) return;
        var cached = plugin.Config.CachedRetainers;
        DictionaryPrune.RemoveKeysWhere(selectedByRetainer, key => !cached.ContainsKey(key));
    }

    private HashSet<uint> FlattenRetainerSelections()
    {
        var ids = new HashSet<uint>();
        foreach (var set in selectedByRetainer.Values)
            foreach (var itemId in set) ids.Add(itemId);
        return ids;
    }

    private int CountInventoryItemsMatchingSelection()
    {
        var queuedItemIds = FlattenRetainerSelections();
        if (queuedItemIds.Count == 0) return 0;

        var matched = 0;
        foreach (var entry in main.InventoryStorable)
            if (queuedItemIds.Contains(entry.ItemId)) matched++;
        return matched;
    }

    private string? ResolveStep2DisabledReason(int step2Count)
    {
        if (step2Count == 0)
        {
            return plugin.Config.DryRun
                ? "Run Step 1 first to queue items for the armoire."
                : "No queued items are in your inventory yet — run Step 1 at the bell.";
        }
        if (plugin.Config.DryRun) return null;
        if (!plugin.Cabinet.IsFresh) return "Open the Armoire once this session to load stored-item data.";
        if (!plugin.Cabinet.IsActivatable) return "Stand near an Armoire to enable.";
        return null;
    }

    private void DrawActionBar()
    {
        var totalSelected = selectedByRetainer.Values.Sum(set => set.Count);
        var willPull = main.ClampForApply(totalSelected);
        MainWindow.DrawInventoryCapWarning(InventorySpace.FreeSlots(), "pull", willPull, totalSelected, "run");

        var canStep1 = main.DryRunOr(willPull > 0);
        ImGui.BeginDisabled(!canStep1);
        if (ImGui.Button($"Step 1: move {willPull} items from retainers to inventory"))
        {
            // Fresh cycle: forget anything queued by a previous dry-run pass.
            if (plugin.Config.DryRun) dryRunPendingArmoireMoves.Clear();

            // Iterate every retainer with selections; the executor is responsible for
            // summoning the right retainer when each MoveFromRetainer call lands.
            var done = 0;
            foreach (var (retainerId, sel) in selectedByRetainer)
            {
                if (done >= willPull) break;
                foreach (var itemId in sel)
                {
                    if (done >= willPull) break;
                    if (plugin.Executor.MoveFromRetainer(itemId, retainerId) != ActionResult.Success) continue;
                    done++;
                    if (plugin.Config.DryRun) dryRunPendingArmoireMoves.Add(itemId);
                }
            }
            plugin.SettingsWindow.OpenOnLogs();
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        // Step 2 acts on what's actually in your bags right now (real mode) or on what
        // dry-run Step 1 just queued (dry-run mode).
        var step2Count = plugin.Config.DryRun
            ? dryRunPendingArmoireMoves.Count
            : CountInventoryItemsMatchingSelection();
        var step2DisabledReason = ResolveStep2DisabledReason(step2Count);
        var canStep2 = step2DisabledReason == null;
        ImGui.BeginDisabled(!canStep2);
        if (ImGui.Button($"Step 2: move {step2Count} items from inventory to Armoire"))
        {
            if (plugin.Config.DryRun)
            {
                foreach (var itemId in dryRunPendingArmoireMoves)
                    plugin.Executor.MoveToArmoire(itemId);
                dryRunPendingArmoireMoves.Clear();
            }
            else
            {
                var queuedItemIds = FlattenRetainerSelections();
                foreach (var entry in main.InventoryStorable)
                    if (queuedItemIds.Contains(entry.ItemId))
                        plugin.Executor.MoveToArmoire(entry.ItemId);
            }
            plugin.SettingsWindow.OpenOnLogs();
        }
        ImGui.EndDisabled();
        if (step2DisabledReason != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(step2DisabledReason);

        if (totalSelected > 0)
        {
            ImGui.SameLine();
            if (ImGui.Button("Clear all##retainers"))
            {
                selectedByRetainer.Clear();
                dryRunPendingArmoireMoves.Clear();
            }
        }
    }

    private void DrawRetainerSection(ulong retainerId, RetainerInventoryCache snap, DateTime now, bool isActive)
    {
        // Filter + dedupe up front so the header count matches what's actually listed below.
        var grouped = GroupRetainerSnapshot(snap);
        if (grouped.Deduped.Count == 0) return;

        if (!selectedByRetainer.TryGetValue(retainerId, out var selection))
            selectedByRetainer[retainerId] = selection = new HashSet<uint>();

        // Drop selected ids that are no longer eligible (e.g. user removed the item from the
        // retainer in-game, or it just got stored in the armoire by another character).
        if (selection.Count > 0)
        {
            var stillEligible = new HashSet<uint>(grouped.Deduped.Count);
            foreach (var entry in grouped.Deduped) stillEligible.Add(entry.ItemId);
            selection.RemoveWhere(id => !stillEligible.Contains(id));
        }

        var displayName = string.IsNullOrEmpty(snap.Name) ? $"Retainer #{retainerId}" : snap.Name;
        var refreshedAgo = snap.RefreshedAt == DateTime.MinValue ? "?" : MainWindow.Humanize(now - snap.RefreshedAt);
        var activeMarker = isActive ? " [active]" : "";
        var selectionMarker = selection.Count > 0 ? $", {selection.Count} selected" : "";
        var status = $"{grouped.Deduped.Count} eligible{selectionMarker}, refreshed {refreshedAgo} ago";
        var headerLabel = $"{displayName}{activeMarker} ({status})###retainer{retainerId}";
        if (!ImGui.CollapsingHeader(headerLabel, ImGuiTreeNodeFlags.DefaultOpen)) return;

        if (ImGui.Button($"Select all##retainer{retainerId}"))
            foreach (var entry in grouped.Deduped) selection.Add(entry.ItemId);
        ImGui.SameLine();
        if (ImGui.Button($"Clear##retainer{retainerId}"))
            selection.Clear();

        ImGui.Spacing();

        foreach (var entry in grouped.Deduped)
            main.DrawSelectableItemRow(entry, $"r{retainerId}_", selection);
    }
}
