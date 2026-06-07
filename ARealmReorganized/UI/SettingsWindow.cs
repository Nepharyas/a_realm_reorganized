using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace ARealmReorganized.UI;

public sealed class SettingsWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private bool autoScrollLogs = true;

    public SettingsWindow(Plugin plugin) : base("A Realm Reorganized - Logs##arrsettings")
    {
        this.plugin = plugin;
        Size = new Vector2(560, 360);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (ImGui.Button("Clear")) plugin.LogBuffer.Clear();
        ImGui.SameLine();
        if (ImGui.Button("Copy")) ImGui.SetClipboardText(plugin.LogBuffer.AsText());
        ImGui.SameLine();
        ImGui.Checkbox("Auto-scroll", ref autoScrollLogs);

        ImGui.Separator();

        if (ImGui.BeginChild("##logarea", Vector2.Zero, true))
        {
            plugin.LogBuffer.ForEach(entry => ImGui.TextUnformatted(entry.Format()));
            if (autoScrollLogs && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 1)
                ImGui.SetScrollHereY(1f);
        }
        ImGui.EndChild();
    }
}
