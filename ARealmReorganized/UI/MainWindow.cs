using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ARealmReorganized.Logic;
using ARealmReorganized.Models;
using ARealmReorganized.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;

namespace ARealmReorganized.UI;

public sealed class MainWindow : Window, IDisposable
{
    private static readonly (string Label, InventorySource Source)[] InventorySectionsInDisplayOrder =
    [
        ("Inventory", InventorySource.Inventory),
        ("Armoury", InventorySource.Armoury),
        ("Saddlebag", InventorySource.Saddlebag),
    ];

    private readonly Plugin plugin;

    private IReadOnlyList<uint> storableCandidates = Array.Empty<uint>();
    private IReadOnlyList<SetGroup> setGroups = Array.Empty<SetGroup>();
    private IReadOnlyList<InventoryEntry> inventoryStorable = Array.Empty<InventoryEntry>();
    private IReadOnlyDictionary<InventorySource, IReadOnlyList<InventoryEntry>> inventoryBySource =
        new Dictionary<InventorySource, IReadOnlyList<InventoryEntry>>();
    private DuplicateDetection.Result duplicates = new()
    {
        MultipleCopies = Array.Empty<DresserItem>(),
        ArmoireRedundant = Array.Empty<DresserItem>(),
    };
    private readonly Dictionary<uint, string> itemNames = new();
    private readonly HashSet<uint> selectedStorableIds = new();
    private readonly HashSet<uint> selectedSetIds = new();
    private readonly HashSet<ushort> selectedDuplicateSlots = new();
    private readonly HashSet<uint> selectedInventoryIds = new();
    private readonly Dictionary<ulong, HashSet<uint>> selectedRetainerItemsByRetainer = new();
    // Items the most recent dry-run Step 1 "would have" moved into inventory. Lets dry-run
    // Step 2 simulate the next stage even though no items actually changed bags.
    private readonly HashSet<uint> dryRunPendingArmoireMoves = new();
    private bool hasScanned;

    public MainWindow(Plugin plugin) : base("A Realm Reorganized##main")
    {
        this.plugin = plugin;
        Size = new Vector2(720, 560);
        SizeCondition = ImGuiCond.FirstUseEver;

        TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Cog,
            IconOffset = new Vector2(2, 1),
            Click = _ => plugin.SettingsWindow.IsOpen = true,
            ShowTooltip = () => ImGui.SetTooltip("Settings"),
        });
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.TextWrapped(
            "Tidy up your glam collection! Scan your Glamour Dresser for items that can be moved to the Armoire, " +
            "detect sets that can be regrouped, free some inventory/retainers/chocobo space.");
        ImGui.Separator();

        DrawServiceStatus();
        ImGui.Spacing();
        DrawScanRow();
        ImGui.Separator();

        var footerHeight = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
        if (ImGui.BeginChild("##body", new Vector2(0, -footerHeight)))
        {
            if (!hasScanned)
            {
                TextDisabledWrapped("Press Scan to populate results.");
            }
            else if (ImGui.BeginTabBar("##arrtabs"))
            {
                if (ImGui.BeginTabItem($"Move to Armoire ({storableCandidates.Count})###armoire"))
                {
                    DrawArmoireTab();
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem($"Compress into sets ({setGroups.Count})###compress"))
                {
                    DrawCompressTab();
                    ImGui.EndTabItem();
                }
                var dupeCount = duplicates.MultipleCopies.Count + duplicates.ArmoireRedundant.Count;
                if (ImGui.BeginTabItem($"Remove duplicates ({dupeCount})###duplicates"))
                {
                    DrawDuplicatesTab();
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem($"Sort from inventory ({inventoryStorable.Count})###inventory"))
                {
                    DrawInventoryTab();
                    ImGui.EndTabItem();
                }
                var retainerEligibleCount = CountEligibleAcrossRetainers();
                if (ImGui.BeginTabItem($"Sort from retainers ({retainerEligibleCount})###retainers"))
                {
                    DrawRetainersTab();
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
        }
        ImGui.EndChild();

        DrawFooter();
    }

    private void DrawFooter()
    {
        ImGui.Separator();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(1f, 0.37f, 0.36f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1f, 0.5f, 0.48f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.85f, 0.3f, 0.3f, 1f));
        if (ImGui.SmallButton("♥ Support on Ko-fi"))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://ko-fi.com/nepharyas",
                UseShellExecute = true,
            });
        }
        ImGui.PopStyleColor(3);
    }

    private void DrawServiceStatus()
    {
        var dresserCache = plugin.Config.CachedDresser;
        var cabinetCache = plugin.Config.CachedCabinet;

        var dresserMsg = dresserCache.RefreshedAt == DateTime.MinValue
            ? "dresser: never seen yet"
            : $"dresser: {Humanize(DateTime.UtcNow - dresserCache.RefreshedAt)} ago ({dresserCache.Slots.Count} items)";
        var cabinetMsg = cabinetCache.RefreshedAt == DateTime.MinValue
            ? "armoire: never seen yet"
            : $"armoire: {Humanize(DateTime.UtcNow - cabinetCache.RefreshedAt)} ago ({cabinetCache.StoredIds.Count} stored)";

        TextDisabledWrapped(
            $"{dresserMsg}    {cabinetMsg}    inventory: {InventorySpace.FreeSlots()} free, {InventorySpace.GlamourPrismCount()} prisms");
    }

    private static string Humanize(TimeSpan ts)
    {
        if (ts.TotalSeconds < 60) return $"{(int)ts.TotalSeconds}s";
        if (ts.TotalMinutes < 60) return $"{(int)ts.TotalMinutes}m";
        if (ts.TotalHours < 24) return $"{(int)ts.TotalHours}h";
        return $"{(int)ts.TotalDays}d";
    }

    private static void TextDisabledWrapped(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    // Dry-run is the universal "always allow the click" bypass; every write-action button
    // checks `DryRun || (its specific gates)`. This keeps the bypass in one place.
    private bool DryRunOr(bool gate) => plugin.Config.DryRun || gate;

    private HashSet<uint> FlattenRetainerSelections()
    {
        var ids = new HashSet<uint>();
        foreach (var set in selectedRetainerItemsByRetainer.Values)
            foreach (var itemId in set) ids.Add(itemId);
        return ids;
    }

    private int CountInventoryItemsMatchingRetainerSelection()
    {
        var queuedItemIds = FlattenRetainerSelections();
        if (queuedItemIds.Count == 0) return 0;

        var matched = 0;
        foreach (var entry in inventoryStorable)
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

    // Cap is well above the eligible-set size for any plausible session; if exceeded we just
    // wipe and rebuild on demand rather than tracking LRU.
    private const int ItemNameCacheCap = 5000;

    private string ResolveItemName(uint itemId)
    {
        if (itemNames.TryGetValue(itemId, out var cached)) return cached;
        if (itemNames.Count >= ItemNameCacheCap) itemNames.Clear();
        var resolved = ItemNames.Resolve(itemId);
        itemNames[itemId] = resolved;
        return resolved;
    }

    private void DrawCabinetUnavailableBanner()
    {
        if (plugin.Cabinet.IsFresh) return;
        ImGui.PushTextWrapPos();
        ImGui.TextColored(UiColors.Warning,
            "Open the Armoire once this session to load stored-item data. Until then, items already in the armoire may show here and apply is disabled.");
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
    }

    private void DrawScanRow()
    {
        if (ImGui.Button("Scan")) RunScan();
        ImGui.SameLine();

        var dryRun = plugin.Config.DryRun;
        if (ImGui.Checkbox("Dry run (preview only — never moves items)", ref dryRun))
        {
            plugin.Config.DryRun = dryRun;
            plugin.Config.Save();
            // Drop the dry-run-only pending queue; it would otherwise carry stale state
            // across the toggle and confuse the Step 2 count in real mode.
            dryRunPendingArmoireMoves.Clear();
        }
    }

    private void DrawArmoireTab()
    {
        DrawCabinetUnavailableBanner();

        if (storableCandidates.Count == 0)
        {
            TextDisabledWrapped("Nothing in your dresser is currently armoire-eligible.");
            return;
        }

        if (ImGui.Button("Select all eligible"))
            foreach (var id in storableCandidates) selectedStorableIds.Add(id);
        ImGui.SameLine();
        if (ImGui.Button("Clear##armoire")) selectedStorableIds.Clear();

        ImGui.Spacing();

        var freeSlots = InventorySpace.FreeSlots();
        var selected = selectedStorableIds.Count;
        var willMove = plugin.Config.DryRun ? selected : System.Math.Min(selected, freeSlots);

        if (selected > 0 && !plugin.Config.DryRun && selected > freeSlots)
        {
            ImGui.TextColored(UiColors.Warning,
                $"Inventory has {freeSlots} free slots — will move {willMove} of {selected} this round. Clear space and re-apply for the rest.");
        }

        var canApply = DryRunOr(plugin.Cabinet.IsFresh && plugin.Cabinet.IsActivatable && plugin.Dresser.IsActivatable);
        canApply = canApply && willMove > 0;
        ImGui.BeginDisabled(!canApply);
        if (ImGui.Button($"Apply: move {willMove} items to Armoire"))
        {
            var done = 0;
            foreach (var id in selectedStorableIds)
            {
                if (done >= willMove) break;
                plugin.Executor.MoveToArmoire(id);
                done++;
            }
            plugin.SettingsWindow.OpenOnLogs();
        }
        ImGui.EndDisabled();
        ImGui.Separator();

        if (ImGui.BeginChild("##armoirelist", Vector2.Zero))
        {
            foreach (var id in storableCandidates)
            {
                var checkedFlag = selectedStorableIds.Contains(id);
                var name = ResolveItemName(id);
                if (ImGui.Checkbox($"{name}##s{id}", ref checkedFlag))
                {
                    if (checkedFlag) selectedStorableIds.Add(id);
                    else selectedStorableIds.Remove(id);
                }
            }
        }
        ImGui.EndChild();
    }

    private void DrawCompressTab()
    {
        var completeSets = setGroups.Where(g => g.Pieces.Count == g.TotalPieces).ToList();
        var partialSets = setGroups.Where(g => g.Pieces.Count < g.TotalPieces).ToList();

        if (completeSets.Count == 0 && partialSets.Count == 0)
        {
            TextDisabledWrapped("No detected sets. Add gear to your dresser and re-scan.");
            return;
        }

        ImGui.BeginDisabled(completeSets.Count == 0);
        if (ImGui.Button("Select all complete sets"))
            foreach (var s in completeSets) selectedSetIds.Add(s.SeriesId);
        ImGui.SameLine();
        if (ImGui.Button("Clear##compress")) selectedSetIds.Clear();
        ImGui.EndDisabled();

        ImGui.Spacing();

        var freeSlots = InventorySpace.FreeSlots();
        var prisms = InventorySpace.GlamourPrismCount();
        var selectedSetsList = completeSets.Where(s => selectedSetIds.Contains(s.SeriesId)).ToList();

        var setsToCompress = selectedSetsList;
        var capReason = string.Empty;
        if (!plugin.Config.DryRun)
        {
            setsToCompress = new List<SetGroup>();
            var slotsUsed = 0;
            foreach (var s in selectedSetsList)
            {
                if (setsToCompress.Count >= prisms) { capReason = "prisms"; break; }
                if (slotsUsed + s.Pieces.Count > freeSlots) { capReason = "inventory"; break; }
                slotsUsed += s.Pieces.Count;
                setsToCompress.Add(s);
            }
            if (setsToCompress.Count < selectedSetsList.Count)
            {
                var msg = capReason == "prisms"
                    ? $"Need {selectedSetsList.Count} prisms total — you have {prisms}. Compressing {setsToCompress.Count} this round."
                    : $"Inventory has {freeSlots} free slots — compressing {setsToCompress.Count} of {selectedSetsList.Count} sets this round.";
                ImGui.TextColored(UiColors.Warning, msg);
            }
        }

        var canApply = DryRunOr(plugin.Cabinet.IsActivatable && plugin.Dresser.IsActivatable);
        canApply = canApply && setsToCompress.Count > 0;
        ImGui.BeginDisabled(!canApply);
        if (ImGui.Button($"Apply: compress {setsToCompress.Count} sets"))
        {
            foreach (var s in setsToCompress)
                plugin.Executor.CompressSet(s);
            plugin.SettingsWindow.OpenOnLogs();
        }
        ImGui.EndDisabled();
        ImGui.Separator();

        if (ImGui.BeginChild("##setlist", Vector2.Zero))
        {
            if (completeSets.Count > 0)
            {
                TextDisabledWrapped($"Complete sets ({completeSets.Count}):");
                foreach (var g in completeSets)
                {
                    var checkedFlag = selectedSetIds.Contains(g.SeriesId);
                    var label = $"{g.Name} — {g.Pieces.Count}/{g.TotalPieces} pieces##c{g.SeriesId}";
                    if (ImGui.Checkbox(label, ref checkedFlag))
                    {
                        if (checkedFlag) selectedSetIds.Add(g.SeriesId);
                        else selectedSetIds.Remove(g.SeriesId);
                    }
                }
            }

            if (partialSets.Count > 0)
            {
                if (completeSets.Count > 0) ImGui.Spacing();
                TextDisabledWrapped($"Partial sets ({partialSets.Count}) — finish to compress:");
                ImGui.BeginDisabled(true);
                foreach (var g in partialSets)
                {
                    var dummy = false;
                    ImGui.Checkbox(
                        $"{g.Name} — {g.Pieces.Count}/{g.TotalPieces} pieces##p{g.SeriesId}",
                        ref dummy);
                }
                ImGui.EndDisabled();
            }
        }
        ImGui.EndChild();
    }

    private void DrawDuplicatesTab()
    {
        DrawCabinetUnavailableBanner();

        if (duplicates.MultipleCopies.Count == 0 && duplicates.ArmoireRedundant.Count == 0)
        {
            TextDisabledWrapped("No duplicates detected.");
            return;
        }

        if (ImGui.Button("Select duplicates (keep one of each)"))
        {
            foreach (var d in duplicates.ArmoireRedundant) selectedDuplicateSlots.Add(d.SlotIndex);
            uint lastId = 0;
            var keptOne = false;
            foreach (var d in duplicates.MultipleCopies)
            {
                if (d.ItemId != lastId) { lastId = d.ItemId; keptOne = false; }
                if (!keptOne) { keptOne = true; continue; }
                selectedDuplicateSlots.Add(d.SlotIndex);
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear##dupes")) selectedDuplicateSlots.Clear();

        ImGui.Spacing();

        var selected = selectedDuplicateSlots.Count;
        var freeSlots = InventorySpace.FreeSlots();
        var willRemove = plugin.Config.DryRun ? selected : System.Math.Min(selected, freeSlots);

        if (selected > 0 && !plugin.Config.DryRun && selected > freeSlots)
        {
            ImGui.TextColored(UiColors.Warning,
                $"Inventory has {freeSlots} free slots — will remove {willRemove} of {selected} this round. Clear space and re-apply for the rest.");
        }
        else if (selected > 0 && !plugin.Config.DryRun)
        {
            TextDisabledWrapped($"Inventory free: {freeSlots} slots.");
        }

        var canApply = DryRunOr(plugin.Cabinet.IsFresh && plugin.Dresser.IsActivatable);
        canApply = canApply && willRemove > 0;
        ImGui.BeginDisabled(!canApply);
        if (ImGui.Button($"Apply: remove {willRemove} duplicates"))
        {
            var dryRun = plugin.Config.DryRun;
            var (newDuplicates, removed) = DuplicateDetection.Apply(
                duplicates, selectedDuplicateSlots, willRemove, plugin.Executor);

            // Don't update the UI when doing a DryRun — the log opens instead.
            if (!dryRun)
            {
                duplicates = newDuplicates;
                foreach (var slot in removed)
                    selectedDuplicateSlots.Remove(slot);
            }
            plugin.SettingsWindow.OpenOnLogs();
        }
        ImGui.EndDisabled();
        ImGui.Separator();

        if (ImGui.BeginChild("##dupelist", Vector2.Zero))
        {
            if (duplicates.ArmoireRedundant.Count > 0)
            {
                TextDisabledWrapped(
                    $"Already in armoire ({duplicates.ArmoireRedundant.Count}) — undyed copies you can drop:");
                foreach (var d in duplicates.ArmoireRedundant)
                {
                    DrawDuplicateRow(d, "a");
                }
            }

            if (duplicates.MultipleCopies.Count > 0)
            {
                if (duplicates.ArmoireRedundant.Count > 0) ImGui.Spacing();
                TextDisabledWrapped(
                    $"Multiple copies in dresser ({duplicates.MultipleCopies.Count}) — pick which to keep:");
                foreach (var d in duplicates.MultipleCopies)
                {
                    DrawDuplicateRow(d, "m");
                }
            }
        }
        ImGui.EndChild();
    }

    private void DrawInventoryTab()
    {
        DrawCabinetUnavailableBanner();

        if (inventoryStorable.Count == 0)
        {
            TextDisabledWrapped("Nothing in your inventory, armoury, or saddlebag is currently armoire-eligible.");
            return;
        }

        if (ImGui.Button("Select all##inventory"))
            foreach (var entry in inventoryStorable) selectedInventoryIds.Add(entry.ItemId);
        ImGui.SameLine();
        if (ImGui.Button("Clear##inventory")) selectedInventoryIds.Clear();

        ImGui.Spacing();

        var canApply = DryRunOr(plugin.Cabinet.IsFresh && plugin.Cabinet.IsActivatable);
        canApply = canApply && selectedInventoryIds.Count > 0;
        ImGui.BeginDisabled(!canApply);
        if (ImGui.Button($"Apply: move {selectedInventoryIds.Count} items to Armoire"))
        {
            foreach (var itemId in selectedInventoryIds)
                plugin.Executor.MoveToArmoire(itemId);
        }
        ImGui.EndDisabled();
        ImGui.Separator();

        if (ImGui.BeginChild("##inventorylist", Vector2.Zero))
        {
            foreach (var (label, source) in InventorySectionsInDisplayOrder)
                DrawInventorySection(label, source);
        }
        ImGui.EndChild();
    }

    private void DrawInventorySection(string label, InventorySource source)
    {
        if (!inventoryBySource.TryGetValue(source, out var itemsInSection)) return;

        var headerLabel = $"{label} ({itemsInSection.Count})###invsection{source}";
        if (!ImGui.CollapsingHeader(headerLabel, ImGuiTreeNodeFlags.DefaultOpen)) return;

        foreach (var entry in itemsInSection)
            DrawSelectableItemRow(entry, "i", selectedInventoryIds);
    }

    private void DrawSelectableItemRow(InventoryEntry entry, string idPrefix, HashSet<uint> selection)
    {
        var checkedFlag = selection.Contains(entry.ItemId);
        var name = ResolveItemName(entry.ItemId);
        var rowLabel = entry.IsHq ? $"{name} HQ" : name;
        if (ImGui.Checkbox($"{rowLabel}##{idPrefix}{entry.ItemId}", ref checkedFlag))
        {
            if (checkedFlag) selection.Add(entry.ItemId);
            else selection.Remove(entry.ItemId);
        }
    }

    private void DrawRetainersTab()
    {
        DrawCabinetUnavailableBanner();

        var cached = plugin.Config.CachedRetainers;
        PruneSelectionsForMissingRetainers();

        if (cached.Count == 0)
        {
            TextDisabledWrapped(
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

        DrawRetainersActionBar();
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

    private void PruneSelectionsForMissingRetainers()
    {
        if (selectedRetainerItemsByRetainer.Count == 0) return;
        var cached = plugin.Config.CachedRetainers;
        DictionaryPrune.RemoveKeysWhere(selectedRetainerItemsByRetainer, key => !cached.ContainsKey(key));
    }

    private void DrawRetainersActionBar()
    {
        var freeSlots = InventorySpace.FreeSlots();

        var totalSelected = 0;
        foreach (var set in selectedRetainerItemsByRetainer.Values) totalSelected += set.Count;
        var willPull = plugin.Config.DryRun ? totalSelected : System.Math.Min(totalSelected, freeSlots);

        if (!plugin.Config.DryRun && totalSelected > freeSlots)
        {
            ImGui.TextColored(UiColors.Warning,
                $"Inventory has {freeSlots} free slots — will pull {willPull} of {totalSelected} this round. Clear space and re-run for the rest.");
        }

        var canStep1 = DryRunOr(willPull > 0);
        ImGui.BeginDisabled(!canStep1);
        if (ImGui.Button($"Step 1: move {willPull} items from retainers to inventory"))
        {
            // Fresh cycle: forget anything queued by a previous dry-run pass.
            if (plugin.Config.DryRun) dryRunPendingArmoireMoves.Clear();

            // Iterate every retainer with selections; the executor is responsible for
            // summoning the right retainer when each MoveFromRetainer call lands.
            var done = 0;
            foreach (var (retainerId, sel) in selectedRetainerItemsByRetainer)
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
            : CountInventoryItemsMatchingRetainerSelection();
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
                foreach (var entry in inventoryStorable)
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
                selectedRetainerItemsByRetainer.Clear();
                dryRunPendingArmoireMoves.Clear();
            }
        }
    }

    private void DrawRetainerSection(ulong retainerId, RetainerInventoryCache snap, DateTime now, bool isActive)
    {
        // Filter + dedupe up front so the header count matches what's actually listed below.
        var grouped = GroupRetainerSnapshot(snap);
        if (grouped.Deduped.Count == 0) return;

        if (!selectedRetainerItemsByRetainer.TryGetValue(retainerId, out var selection))
            selectedRetainerItemsByRetainer[retainerId] = selection = new HashSet<uint>();

        // Drop selected ids that are no longer eligible (e.g. user removed the item from the
        // retainer in-game, or it just got stored in the armoire by another character).
        if (selection.Count > 0)
        {
            var stillEligible = new HashSet<uint>(grouped.Deduped.Count);
            foreach (var entry in grouped.Deduped) stillEligible.Add(entry.ItemId);
            selection.RemoveWhere(id => !stillEligible.Contains(id));
        }

        var displayName = string.IsNullOrEmpty(snap.Name) ? $"Retainer #{retainerId}" : snap.Name;
        var refreshedAgo = snap.RefreshedAt == DateTime.MinValue ? "?" : Humanize(now - snap.RefreshedAt);
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
            DrawSelectableItemRow(entry, $"r{retainerId}_", selection);
    }

    private void DrawDuplicateRow(DresserItem d, string idPrefix)
    {
        var checkedFlag = selectedDuplicateSlots.Contains(d.SlotIndex);
        if (ImGui.Checkbox($"##{idPrefix}{d.SlotIndex}", ref checkedFlag))
        {
            if (checkedFlag) selectedDuplicateSlots.Add(d.SlotIndex);
            else selectedDuplicateSlots.Remove(d.SlotIndex);
        }
        ImGui.SameLine();
        DrawDyeSwatch(d.Stain0, d.SlotIndex * 2);
        ImGui.SameLine(0, 2);
        DrawDyeSwatch(d.Stain1, d.SlotIndex * 2 + 1);
        ImGui.SameLine();
        var name = ResolveItemName(d.ItemId);
        ImGui.TextUnformatted(name);
    }

    private static void DrawDyeSwatch(byte stainId, int discriminator)
    {
        var size = new Vector2(14, 14);
        if (stainId == 0)
        {
            var pos = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            var bg = ImGui.ColorConvertFloat4ToU32(new Vector4(0.18f, 0.18f, 0.18f, 1f));
            var line = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.55f, 0.55f, 1f));
            drawList.AddRectFilled(pos, pos + size, bg);
            drawList.AddRect(pos, pos + size, line);
            drawList.AddLine(pos, new Vector2(pos.X + size.X, pos.Y + size.Y), line, 1.2f);
            ImGui.InvisibleButton($"##nodye{discriminator}", size);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Undyed");
            return;
        }
        var sheet = Service.DataManager.GetExcelSheet<Stain>();
        if (sheet is null) { ImGui.Dummy(size); return; }
        var row = sheet.GetRowOrDefault(stainId);
        if (row is null) { ImGui.Dummy(size); return; }

        var color = row.Value.Color;
        var v4 = new Vector4(
            ((color >> 16) & 0xFF) / 255f,
            ((color >> 8) & 0xFF) / 255f,
            (color & 0xFF) / 255f,
            1f);
        ImGui.ColorButton(
            $"##stain{stainId}_{discriminator}", v4,
            ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoBorder,
            size);
        if (ImGui.IsItemHovered())
        {
            var name = row.Value.Name.ExtractText();
            if (!string.IsNullOrWhiteSpace(name)) ImGui.SetTooltip(name);
        }
    }

    private void RunScan()
    {
        var snapshot = plugin.Dresser.Snapshot();
        storableCandidates = plugin.Cabinet.ListStorable(snapshot);
        setGroups = SetCompression.GroupBySeries(snapshot, 2);
        duplicates = DuplicateDetection.Find(snapshot, plugin.Cabinet);
        var grouped = InventoryGrouping.FilterAndGroup(
            InventoryReader.ReadAll(),
            entry => plugin.Cabinet.IsStorable(entry.ItemId));
        inventoryStorable = grouped.Deduped;
        inventoryBySource = grouped.BySource;

        itemNames.Clear();
        var allIds = new HashSet<uint>(storableCandidates);
        foreach (var dresserItem in duplicates.MultipleCopies) allIds.Add(dresserItem.ItemId);
        foreach (var dresserItem in duplicates.ArmoireRedundant) allIds.Add(dresserItem.ItemId);
        foreach (var inventoryEntry in inventoryStorable) allIds.Add(inventoryEntry.ItemId);
        foreach (var snap in plugin.Config.CachedRetainers.Values)
            foreach (var cached in snap.Entries) allIds.Add(cached.ItemId);
        foreach (var itemId in allIds) itemNames[itemId] = ItemNames.Resolve(itemId);

        selectedStorableIds.Clear();
        selectedSetIds.Clear();
        selectedDuplicateSlots.Clear();
        selectedInventoryIds.Clear();
        dryRunPendingArmoireMoves.Clear();
        hasScanned = true;
        var scanMsg =
            $"Scan: {snapshot.Count} dresser items, {storableCandidates.Count} storable, " +
            $"{setGroups.Count} set groups, " +
            $"{duplicates.MultipleCopies.Count + duplicates.ArmoireRedundant.Count} duplicates, " +
            $"{inventoryStorable.Count} from inventory.";
        Service.Log.Information(scanMsg);
        plugin.LogBuffer.Add(scanMsg);
    }
}
