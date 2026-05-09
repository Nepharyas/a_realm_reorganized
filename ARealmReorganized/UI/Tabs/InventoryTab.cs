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

    private readonly MainWindow main;

    public InventoryTab(MainWindow main)
    {
        this.main = main;
    }

    public string TabLabel => $"Sort from inventory ({main.InventoryStorable.Count})###inventory";

    public void Draw()
    {
        main.DrawCabinetUnavailableBanner();

        if (main.InventoryStorable.Count == 0)
        {
            MainWindow.TextDisabledWrapped("Nothing in your inventory, armoury, or saddlebag is currently armoire-eligible.");
            return;
        }

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
            main.DrawItemRow(entry);
    }
}
