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
    private readonly Plugin plugin;

    private IReadOnlyList<uint> storableCandidates = Array.Empty<uint>();
    private IReadOnlyList<SetGroup> setGroups = Array.Empty<SetGroup>();
    private IReadOnlyList<InventoryEntry> inventoryStorable = Array.Empty<InventoryEntry>();
    private readonly Dictionary<InventorySource, List<InventoryEntry>> inventoryBySource = new();
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

    private void DrawScanRow()
    {
        if (ImGui.Button("Scan")) RunScan();
        ImGui.SameLine();

        var dryRun = plugin.Config.DryRun;
        if (ImGui.Checkbox("Dry run (preview only — never moves items)", ref dryRun))
        {
            plugin.Config.DryRun = dryRun;
            plugin.Config.Save();
        }
    }

    private void DrawArmoireTab()
    {
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
            ImGui.TextColored(new Vector4(1f, 0.65f, 0.2f, 1f),
                $"Inventory has {freeSlots} free slots — will move {willMove} of {selected} this round. Clear space and re-apply for the rest.");
        }

        var canApply = plugin.Config.DryRun || (plugin.Cabinet.IsActivatable && plugin.Dresser.IsActivatable);
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
                var name = itemNames.GetValueOrDefault(id, $"Item #{id}");
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
                ImGui.TextColored(new Vector4(1f, 0.65f, 0.2f, 1f), msg);
            }
        }

        var canApply = plugin.Config.DryRun || (plugin.Cabinet.IsActivatable && plugin.Dresser.IsActivatable);
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
            ImGui.TextColored(new Vector4(1f, 0.65f, 0.2f, 1f),
                $"Inventory has {freeSlots} free slots — will remove {willRemove} of {selected} this round. Clear space and re-apply for the rest.");
        }
        else if (selected > 0 && !plugin.Config.DryRun)
        {
            TextDisabledWrapped($"Inventory free: {freeSlots} slots.");
        }

        var canApply = plugin.Config.DryRun || plugin.Dresser.IsActivatable;
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
        if (inventoryStorable.Count == 0)
        {
            TextDisabledWrapped("Nothing in your inventory, armoury, or saddlebag is currently armoire-eligible.");
            return;
        }

        if (ImGui.Button("Select all"))
            foreach (var entry in inventoryStorable) selectedInventoryIds.Add(entry.ItemId);
        ImGui.SameLine();
        if (ImGui.Button("Clear##inventory")) selectedInventoryIds.Clear();

        ImGui.Spacing();

        var canApply = plugin.Config.DryRun || plugin.Cabinet.IsActivatable;
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
            DrawInventorySection("Inventory", InventorySource.Inventory);
            DrawInventorySection("Armoury", InventorySource.Armoury);
            DrawInventorySection("Saddlebag", InventorySource.Saddlebag);
        }
        ImGui.EndChild();
    }

    private void DrawInventorySection(string label, InventorySource source)
    {
        if (!inventoryBySource.TryGetValue(source, out var itemsInSection)) return;
        if (itemsInSection.Count == 0) return;

        var headerLabel = $"{label} ({itemsInSection.Count})###invsection{source}";
        if (!ImGui.CollapsingHeader(headerLabel, ImGuiTreeNodeFlags.DefaultOpen)) return;

        foreach (var entry in itemsInSection)
        {
            var checkedFlag = selectedInventoryIds.Contains(entry.ItemId);
            var name = itemNames.GetValueOrDefault(entry.ItemId, $"Item #{entry.ItemId}");
            if (ImGui.Checkbox($"{name}##i{entry.ItemId}", ref checkedFlag))
            {
                if (checkedFlag) selectedInventoryIds.Add(entry.ItemId);
                else selectedInventoryIds.Remove(entry.ItemId);
            }
        }
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
        var name = itemNames.GetValueOrDefault(d.ItemId, $"Item #{d.ItemId}");
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
        inventoryStorable = ScanInventoryForStorable();

        itemNames.Clear();
        var itemSheet = Service.DataManager.GetExcelSheet<Item>();
        if (itemSheet is not null)
        {
            var allIds = new HashSet<uint>(storableCandidates);
            foreach (var dresserItem in duplicates.MultipleCopies) allIds.Add(dresserItem.ItemId);
            foreach (var dresserItem in duplicates.ArmoireRedundant) allIds.Add(dresserItem.ItemId);
            foreach (var inventoryEntry in inventoryStorable) allIds.Add(inventoryEntry.ItemId);

            foreach (var itemId in allIds)
            {
                var row = itemSheet.GetRowOrDefault(itemId);
                if (row is not null) itemNames[itemId] = row.Value.Name.ExtractText();
            }
        }

        selectedStorableIds.Clear();
        selectedSetIds.Clear();
        selectedDuplicateSlots.Clear();
        selectedInventoryIds.Clear();
        hasScanned = true;
        var scanMsg =
            $"Scan: {snapshot.Count} dresser items, {storableCandidates.Count} storable, " +
            $"{setGroups.Count} set groups, " +
            $"{duplicates.MultipleCopies.Count + duplicates.ArmoireRedundant.Count} duplicates, " +
            $"{inventoryStorable.Count} from inventory.";
        Service.Log.Information(scanMsg);
        plugin.LogBuffer.Add(scanMsg);
    }

    private IReadOnlyList<InventoryEntry> ScanInventoryForStorable()
    {
        var allInventoryEntries = InventoryReader.ReadAll();
        var result = new List<InventoryEntry>();
        var seenItemIds = new HashSet<uint>();
        foreach (var inventoryEntry in allInventoryEntries)
        {
            if (!plugin.Cabinet.IsStorable(inventoryEntry.ItemId)) continue;
            if (seenItemIds.Add(inventoryEntry.ItemId)) result.Add(inventoryEntry);
        }

        inventoryBySource.Clear();
        foreach (var inventoryEntry in result)
        {
            if (!inventoryBySource.TryGetValue(inventoryEntry.Source, out var list))
                inventoryBySource[inventoryEntry.Source] = list = new List<InventoryEntry>();
            list.Add(inventoryEntry);
        }
        return result;
    }
}
