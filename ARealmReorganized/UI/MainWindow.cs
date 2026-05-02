using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ARealmReorganized.Logic;
using ARealmReorganized.Models;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;

namespace ARealmReorganized.UI;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    private IReadOnlyList<uint> storableCandidates = Array.Empty<uint>();
    private IReadOnlyList<SetGroup> setGroups = Array.Empty<SetGroup>();
    private DuplicateDetection.Result duplicates = new()
    {
        MultipleCopies = Array.Empty<DresserItem>(),
        ArmoireRedundant = Array.Empty<DresserItem>(),
    };
    private readonly Dictionary<uint, string> itemNames = new();
    private readonly HashSet<uint> selectedStorableIds = new();
    private readonly HashSet<uint> selectedSetIds = new();
    private readonly HashSet<ushort> selectedDuplicateSlots = new();
    private bool hasScanned;

    public MainWindow(Plugin plugin) : base("A Realm Reorganized##main")
    {
        this.plugin = plugin;
        Size = new Vector2(720, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.TextWrapped(
            "Scan your Glamour Dresser for items that can be moved to the Armoire " +
            "and detect partial sets that can be regrouped. Nothing happens until you press Apply.");
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
                ImGui.TextDisabled("Press Scan to populate results.");
            }
            else if (ImGui.BeginTabBar("##arrtabs"))
            {
                if (ImGui.BeginTabItem($"Move to Armoire ({storableCandidates.Count})"))
                {
                    DrawArmoireTab();
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem($"Compress into sets ({setGroups.Count})"))
                {
                    DrawCompressTab();
                    ImGui.EndTabItem();
                }
                var dupeCount = duplicates.MultipleCopies.Count + duplicates.ArmoireRedundant.Count;
                if (ImGui.BeginTabItem($"Remove duplicates ({dupeCount})"))
                {
                    DrawDuplicatesTab();
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

        ImGui.TextDisabled(
            $"{dresserMsg}    {cabinetMsg}    inventory: {InventorySpace.FreeSlots()} free, {InventorySpace.GlamourPrismCount()} prisms");
        ImGui.TextDisabled($"Armoire-eligible items in current game data: {plugin.Eligibility.Count}");
    }

    private static string Humanize(TimeSpan ts)
    {
        if (ts.TotalSeconds < 60) return $"{(int)ts.TotalSeconds}s";
        if (ts.TotalMinutes < 60) return $"{(int)ts.TotalMinutes}m";
        if (ts.TotalHours < 24) return $"{(int)ts.TotalHours}h";
        return $"{(int)ts.TotalDays}d";
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

        if (ImGui.CollapsingHeader("Settings"))
        {
            var threshold = plugin.Config.MultiRoundThreshold;
            ImGui.SetNextItemWidth(150);
            if (ImGui.SliderInt("Stop multi-round transfer when free inventory drops below", ref threshold, 1, 30))
            {
                plugin.Config.MultiRoundThreshold = threshold;
                plugin.Config.Save();
            }
            ImGui.TextDisabled(
                "When applying a Move or Compress that exceeds your free slots, the plugin will run several rounds " +
                "(transferring as many as fit, waiting for inventory to clear, then continuing). It pauses when " +
                "free slots drop below the threshold above to avoid slow trickle transfers.");
        }
    }

    private void DrawArmoireTab()
    {
        if (storableCandidates.Count == 0)
        {
            ImGui.TextDisabled("Nothing in your dresser is currently armoire-eligible.");
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
            ImGui.TextDisabled("No detected sets. Add gear to your dresser and re-scan.");
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
                    ? $"You have {prisms} glamour prisms — will compress {setsToCompress.Count} of {selectedSetsList.Count} sets this round. Get more prisms for the rest."
                    : $"Inventory has {freeSlots} free slots — will compress {setsToCompress.Count} of {selectedSetsList.Count} sets this round. Clear space and re-apply.";
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
        }
        ImGui.EndDisabled();
        ImGui.Separator();

        if (ImGui.BeginChild("##setlist", Vector2.Zero))
        {
            if (completeSets.Count > 0)
            {
                ImGui.TextDisabled($"Complete sets ({completeSets.Count}):");
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
                ImGui.TextDisabled($"Partial sets ({partialSets.Count}) — finish to compress:");
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
            ImGui.TextDisabled("No duplicates detected.");
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
            ImGui.TextDisabled($"Inventory free: {freeSlots} slots.");
        }

        var canApply = plugin.Config.DryRun || plugin.Dresser.IsActivatable;
        canApply = canApply && willRemove > 0;
        ImGui.BeginDisabled(!canApply);
        if (ImGui.Button($"Apply: remove {willRemove} duplicates"))
        {
            var done = 0;
            foreach (var d in duplicates.ArmoireRedundant)
            {
                if (done >= willRemove) break;
                if (!selectedDuplicateSlots.Contains(d.SlotIndex)) continue;
                plugin.Executor.RemoveFromDresser(d);
                done++;
            }
            foreach (var d in duplicates.MultipleCopies)
            {
                if (done >= willRemove) break;
                if (!selectedDuplicateSlots.Contains(d.SlotIndex)) continue;
                plugin.Executor.RemoveFromDresser(d);
                done++;
            }
        }
        ImGui.EndDisabled();
        ImGui.Separator();

        if (ImGui.BeginChild("##dupelist", Vector2.Zero))
        {
            if (duplicates.ArmoireRedundant.Count > 0)
            {
                ImGui.TextDisabled(
                    $"Already in armoire ({duplicates.ArmoireRedundant.Count}) — undyed copies you can drop:");
                foreach (var d in duplicates.ArmoireRedundant)
                {
                    DrawDuplicateRow(d, "a");
                }
            }

            if (duplicates.MultipleCopies.Count > 0)
            {
                if (duplicates.ArmoireRedundant.Count > 0) ImGui.Spacing();
                ImGui.TextDisabled(
                    $"Multiple copies in dresser ({duplicates.MultipleCopies.Count}) — pick which to keep:");
                foreach (var d in duplicates.MultipleCopies)
                {
                    DrawDuplicateRow(d, "m");
                }
            }
        }
        ImGui.EndChild();
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

        itemNames.Clear();
        var itemSheet = Service.DataManager.GetExcelSheet<Item>();
        if (itemSheet is not null)
        {
            var allIds = new HashSet<uint>(storableCandidates);
            foreach (var d in duplicates.MultipleCopies) allIds.Add(d.ItemId);
            foreach (var d in duplicates.ArmoireRedundant) allIds.Add(d.ItemId);

            foreach (var id in allIds)
            {
                var row = itemSheet.GetRowOrDefault(id);
                if (row is not null) itemNames[id] = row.Value.Name.ExtractText();
            }
        }

        selectedStorableIds.Clear();
        selectedSetIds.Clear();
        selectedDuplicateSlots.Clear();
        hasScanned = true;
        Service.Log.Information(
            $"Scan: {snapshot.Count} dresser items, {storableCandidates.Count} storable, " +
            $"{setGroups.Count} set groups, " +
            $"{duplicates.MultipleCopies.Count + duplicates.ArmoireRedundant.Count} duplicates.");
    }
}
