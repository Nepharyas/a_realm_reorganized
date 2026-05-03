using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace ARealmReorganized.UI;

public sealed class SettingsWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private bool autoScrollLogs = true;
    private bool jumpToLogs;

    public SettingsWindow(Plugin plugin) : base("A Realm Reorganized — Settings##arrsettings")
    {
        this.plugin = plugin;
        Size = new Vector2(560, 360);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public void OpenOnLogs()
    {
        IsOpen = true;
        jumpToLogs = true;
    }

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("##settingstabs")) return;

        if (ImGui.BeginTabItem("General"))
        {
            DrawGeneral();
            ImGui.EndTabItem();
        }

        var logsFlags = jumpToLogs ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        var logsOpen = true;
        if (ImGui.BeginTabItem("Logs", ref logsOpen, logsFlags))
        {
            DrawLogs();
            ImGui.EndTabItem();
        }
        if (jumpToLogs) jumpToLogs = false;

        ImGui.EndTabBar();
    }

    private void DrawGeneral()
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

    private void DrawLogs()
    {
        if (ImGui.Button("Clear")) plugin.LogBuffer.Clear();
        ImGui.SameLine();
        ImGui.Checkbox("Auto-scroll", ref autoScrollLogs);

        ImGui.Separator();

        if (ImGui.BeginChild("##logarea", Vector2.Zero, true))
        {
            foreach (var line in plugin.LogBuffer.Snapshot())
                ImGui.TextUnformatted(line);
            if (autoScrollLogs)
                ImGui.SetScrollHereY(1f);
        }
        ImGui.EndChild();
    }
}
