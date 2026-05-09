using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace ARealmReorganized.UI.Tabs;

internal sealed class CompressTab
{
    private readonly MainWindow main;

    public CompressTab(MainWindow main)
    {
        this.main = main;
    }

    public string TabLabel => $"Compress into sets ({main.SetGroups.Count})###compress";

    public void Reset() { }

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
            if (completeSets.Count > 0)
            {
                MainWindow.TextDisabledWrapped($"Complete sets ({completeSets.Count}):");
                foreach (var g in completeSets)
                    ImGui.TextUnformatted($"{g.Name} — {g.Pieces.Count}/{g.TotalPieces} pieces");
            }

            if (partialSets.Count > 0)
            {
                if (completeSets.Count > 0) ImGui.Spacing();
                MainWindow.TextDisabledWrapped($"Partial sets ({partialSets.Count}) — finish to compress:");
                foreach (var g in partialSets)
                    ImGui.TextUnformatted($"{g.Name} — {g.Pieces.Count}/{g.TotalPieces} pieces");
            }
        }
        ImGui.EndChild();
    }
}
