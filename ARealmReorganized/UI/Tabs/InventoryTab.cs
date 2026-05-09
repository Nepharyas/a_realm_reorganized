using System.Collections.Generic;
using System.Numerics;
using ARealmReorganized.Models;
using Dalamud.Bindings.ImGui;

namespace ARealmReorganized.UI.Tabs;

internal sealed class InventoryTab
{
    private static readonly (string Label, InventorySource Source)[] SectionsInDisplayOrder =
    [
        ("Inventory", InventorySource.Inventory),
        ("Armoury", InventorySource.Armoury),
        ("Saddlebag", InventorySource.Saddlebag),
    ];

    private readonly Plugin plugin;
    private readonly MainWindow main;
    private readonly HashSet<uint> selectedIds = new();

    public InventoryTab(Plugin plugin, MainWindow main)
    {
        this.plugin = plugin;
        this.main = main;
    }

    internal HashSet<uint> SelectedIds => selectedIds;

    public string TabLabel => $"Sort from inventory ({main.InventoryStorable.Count})###inventory";

    public void Reset() => selectedIds.Clear();

    public void Draw()
    {
        main.DrawCabinetUnavailableBanner();

        var storable = main.InventoryStorable;
        if (storable.Count == 0)
        {
            MainWindow.TextDisabledWrapped("Nothing in your inventory, armoury, or saddlebag is currently armoire-eligible.");
            return;
        }

        if (ImGui.Button("Select all##inventory"))
            foreach (var entry in storable) selectedIds.Add(entry.ItemId);
        ImGui.SameLine();
        if (ImGui.Button("Clear##inventory")) selectedIds.Clear();

        ImGui.Spacing();

        var canApply = main.DryRunOr(plugin.Cabinet.IsFresh && plugin.Cabinet.IsActivatable);
        canApply = canApply && selectedIds.Count > 0;
        ImGui.BeginDisabled(!canApply);
        if (ImGui.Button($"Apply: move {selectedIds.Count} items to Armoire"))
        {
            foreach (var itemId in selectedIds)
                plugin.Executor.MoveToArmoire(itemId);
        }
        ImGui.EndDisabled();
        ImGui.Separator();

        if (ImGui.BeginChild("##inventorylist", Vector2.Zero))
        {
            foreach (var (label, source) in SectionsInDisplayOrder)
                DrawSection(label, source);
        }
        ImGui.EndChild();
    }

    private void DrawSection(string label, InventorySource source)
    {
        if (!main.InventoryBySource.TryGetValue(source, out var itemsInSection)) return;

        var headerLabel = $"{label} ({itemsInSection.Count})###invsection{source}";
        if (!ImGui.CollapsingHeader(headerLabel, ImGuiTreeNodeFlags.DefaultOpen)) return;

        foreach (var entry in itemsInSection)
            main.DrawSelectableItemRow(entry, "i", selectedIds);
    }
}
