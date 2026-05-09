using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ARealmReorganized.Logic;
using ARealmReorganized.Models;
using Dalamud.Bindings.ImGui;

namespace ARealmReorganized.UI.Tabs;

internal sealed class CompressTab
{
    private readonly Plugin plugin;
    private readonly MainWindow main;
    private readonly HashSet<uint> selectedSetIds = new();

    public CompressTab(Plugin plugin, MainWindow main)
    {
        this.plugin = plugin;
        this.main = main;
    }

    public string TabLabel => $"Compress into sets ({main.SetGroups.Count})###compress";

    public void Reset() => selectedSetIds.Clear();

    public void Draw()
    {
        var sets = main.SetGroups;
        var completeSets = sets.Where(g => g.Pieces.Count == g.TotalPieces).ToList();
        var partialSets = sets.Where(g => g.Pieces.Count < g.TotalPieces).ToList();

        if (completeSets.Count == 0 && partialSets.Count == 0)
        {
            MainWindow.TextDisabledWrapped("No detected sets. Add gear to your dresser and re-scan.");
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

        var canApply = main.DryRunOr(plugin.Cabinet.IsActivatable && plugin.Dresser.IsActivatable);
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
                MainWindow.TextDisabledWrapped($"Complete sets ({completeSets.Count}):");
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
                MainWindow.TextDisabledWrapped($"Partial sets ({partialSets.Count}) — finish to compress:");
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
}
