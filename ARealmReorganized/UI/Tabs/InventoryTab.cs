using System.Numerics;
using ARealmReorganized.Models;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

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

    public string TabLabel => $"Inventory → Armoire ({main.InventoryStorable.Count})###inventory";

    public void Draw()
    {
        main.DrawCabinetUnavailableBanner();
        main.DrawSaddlebagUnavailableBanner();

        if (main.InventoryStorable.Count == 0)
        {
            MainWindow.TextDisabledWrapped("Nothing in your inventory, armoury, or saddlebag is currently armoire-eligible.");
            return;
        }

        using var list = ImRaii.Child("##inventorylist", Vector2.Zero);
        if (list)
        {
            foreach (var (label, source) in SectionsInDisplayOrder)
                DrawSection(label, source);
        }
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
