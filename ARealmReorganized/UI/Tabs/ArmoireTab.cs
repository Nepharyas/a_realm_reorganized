using System.Collections.Generic;
using System.Numerics;
using ARealmReorganized.Logic;
using Dalamud.Bindings.ImGui;

namespace ARealmReorganized.UI.Tabs;

internal sealed class ArmoireTab
{
    private readonly Plugin plugin;
    private readonly MainWindow main;
    private readonly HashSet<uint> selectedIds = new();

    public ArmoireTab(Plugin plugin, MainWindow main)
    {
        this.plugin = plugin;
        this.main = main;
    }

    public string TabLabel => $"Move to Armoire ({main.StorableCandidates.Count})###armoire";

    public void Reset() => selectedIds.Clear();

    public void Draw()
    {
        main.DrawCabinetUnavailableBanner();

        var storable = main.StorableCandidates;
        if (storable.Count == 0)
        {
            MainWindow.TextDisabledWrapped("Nothing in your dresser is currently armoire-eligible.");
            return;
        }

        if (ImGui.Button("Select all eligible"))
            foreach (var id in storable) selectedIds.Add(id);
        ImGui.SameLine();
        if (ImGui.Button("Clear##armoire")) selectedIds.Clear();

        ImGui.Spacing();

        var selected = selectedIds.Count;
        var willMove = main.ClampForApply(selected);
        MainWindow.DrawInventoryCapWarning(InventorySpace.FreeSlots(), "move", willMove, selected, "apply");

        var canApply = main.DryRunOr(plugin.Cabinet.IsFresh && plugin.Cabinet.IsActivatable && plugin.Dresser.IsActivatable);
        canApply = canApply && willMove > 0;
        ImGui.BeginDisabled(!canApply);
        if (ImGui.Button($"Apply: move {willMove} items to Armoire"))
        {
            var done = 0;
            foreach (var id in selectedIds)
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
            foreach (var id in storable)
            {
                var checkedFlag = selectedIds.Contains(id);
                var name = main.ResolveItemName(id);
                if (ImGui.Checkbox($"{name}##s{id}", ref checkedFlag))
                {
                    if (checkedFlag) selectedIds.Add(id);
                    else selectedIds.Remove(id);
                }
            }
        }
        ImGui.EndChild();
    }
}
