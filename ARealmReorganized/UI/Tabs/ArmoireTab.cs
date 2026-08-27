using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

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

        using var list = ImRaii.Child("##armoirelist", Vector2.Zero);
        if (list)
        {
            foreach (var id in storable)
                ImGui.TextUnformatted(main.ResolveItemName(id));
        }
    }
}
