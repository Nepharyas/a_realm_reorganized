using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ARealmReorganized.Models;
using Dalamud.Bindings.ImGui;

namespace ARealmReorganized.UI.Tabs;

internal sealed class CompressTab
{
    private readonly MainWindow main;

    public CompressTab(MainWindow main)
    {
        this.main = main;
    }

    public string TabLabel => $"Sets ({main.SetGroups.Count})###compress";

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

        if (ImGui.BeginChild("##setlist", Vector2.Zero))
        {
            DrawSetSection("Complete sets", "completesets", completeSets);
            DrawSetSection("Incomplete sets", "incompletesets", partialSets);
        }
        ImGui.EndChild();
    }

    private static void DrawSetSection(string label, string sectionId, List<SetGroup> sets)
    {
        if (sets.Count == 0) return;
        var headerLabel = $"{label} ({sets.Count})###{sectionId}";
        if (!ImGui.CollapsingHeader(headerLabel, ImGuiTreeNodeFlags.DefaultOpen)) return;

        foreach (var g in sets)
            ImGui.TextUnformatted($"{g.Name}: {g.Pieces.Count}/{g.TotalPieces} pieces");
    }
}
