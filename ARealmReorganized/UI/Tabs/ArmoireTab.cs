using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace ARealmReorganized.UI.Tabs;

internal sealed class ArmoireTab
{
    private readonly MainWindow main;

    public ArmoireTab(MainWindow main)
    {
        this.main = main;
    }

    public string TabLabel => $"Dresser → Armoire ({main.StorableCandidates.Count})###armoire";

    public void Draw()
    {
        main.DrawCabinetUnavailableBanner();

        var storable = main.StorableCandidates;
        if (storable.Count == 0)
        {
            MainWindow.TextDisabledWrapped("Nothing in your dresser is currently armoire-eligible.");
            return;
        }

        if (ImGui.BeginChild("##armoirelist", Vector2.Zero))
        {
            foreach (var id in storable)
                ImGui.TextUnformatted(main.ResolveItemName(id));
        }
        ImGui.EndChild();
    }
}
