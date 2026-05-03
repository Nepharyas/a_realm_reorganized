using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace ARealmReorganized.UI;

public sealed class SettingsWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public SettingsWindow(Plugin plugin) : base("A Realm Reorganized — Settings##arrsettings")
    {
        this.plugin = plugin;
        Size = new Vector2(440, 240);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var threshold = plugin.Config.MultiRoundThreshold;
        ImGui.SetNextItemWidth(150);
        if (ImGui.SliderInt("Stop multi-round transfer when free inventory drops below", ref threshold, 1, 30))
        {
            plugin.Config.MultiRoundThreshold = threshold;
            plugin.Config.Save();
        }
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(
            "When applying a Move or Compress that exceeds your free slots, plugin will run several rounds " +
            "(transferring as many as it can, waiting for inventory to clear, then continuing). It pauses when " +
            "free slots drop below the threshold above to avoid slow trickle transfers.");
        ImGui.PopStyleColor();
    }
}
