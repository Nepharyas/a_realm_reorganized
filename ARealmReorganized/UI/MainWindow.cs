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
    private readonly Dictionary<uint, string> itemNames = new();
    private readonly HashSet<uint> selectedStorableIds = new();
    private readonly HashSet<uint> selectedSetIds = new();
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
        if (plugin.Cabinet.IsAvailable && plugin.Dresser.IsAvailable)
            ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), "Game services connected.");
        else
            ImGui.TextColored(new Vector4(1f, 0.65f, 0.2f, 1f),
                "Armoire/Dresser readers are stubs. Real game integration coming once ClientStructs settles on 7.5.");
        ImGui.TextDisabled($"Armoire-eligible items in current game data: {plugin.Eligibility.Count}");
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
            ImGui.TextDisabled("Nothing in your dresser is currently armoire-eligible.");
            return;
        }

        if (ImGui.Button("Select all eligible"))
            foreach (var id in storableCandidates) selectedStorableIds.Add(id);
        ImGui.SameLine();
        if (ImGui.Button("Clear##armoire")) selectedStorableIds.Clear();

        ImGui.Spacing();

        var canApply = plugin.Config.DryRun || (plugin.Cabinet.IsAvailable && plugin.Dresser.IsAvailable);
        canApply = canApply && selectedStorableIds.Count > 0;
        ImGui.BeginDisabled(!canApply);
        if (ImGui.Button($"Apply: move {selectedStorableIds.Count} items to Armoire"))
        {
            foreach (var id in selectedStorableIds)
                plugin.Executor.MoveToArmoire(id);
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

        var canApply = plugin.Config.DryRun || (plugin.Cabinet.IsAvailable && plugin.Dresser.IsAvailable);
        canApply = canApply && selectedSetIds.Count > 0;
        ImGui.BeginDisabled(!canApply);
        if (ImGui.Button($"Apply: compress {selectedSetIds.Count} sets"))
        {
            foreach (var s in completeSets)
                if (selectedSetIds.Contains(s.SeriesId)) plugin.Executor.CompressSet(s);
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

    private void RunScan()
    {
        var snapshot = plugin.Dresser.Snapshot();
        storableCandidates = plugin.Cabinet.ListStorable(snapshot);
        setGroups = SetCompression.GroupBySeries(snapshot, 2);

        itemNames.Clear();
        var itemSheet = Service.DataManager.GetExcelSheet<Item>();
        if (itemSheet is not null)
        {
            foreach (var id in storableCandidates)
            {
                var row = itemSheet.GetRowOrDefault(id);
                if (row is not null) itemNames[id] = row.Value.Name.ExtractText();
            }
        }

        selectedStorableIds.Clear();
        selectedSetIds.Clear();
        hasScanned = true;
        Service.Log.Information(
            $"Scan: {snapshot.Count} dresser items, {storableCandidates.Count} storable, {setGroups.Count} set groups.");
    }
}
