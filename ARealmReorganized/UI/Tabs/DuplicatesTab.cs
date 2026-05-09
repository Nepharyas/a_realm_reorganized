using System.Numerics;
using ARealmReorganized.Models;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;

namespace ARealmReorganized.UI.Tabs;

internal sealed class DuplicatesTab
{
    private readonly MainWindow main;

    public DuplicatesTab(MainWindow main)
    {
        this.main = main;
    }

    public string TabLabel
    {
        get
        {
            var dupeCount = main.Duplicates.MultipleCopies.Count + main.Duplicates.ArmoireRedundant.Count;
            return $"Duplicates ({dupeCount})###duplicates";
        }
    }

    public void Draw()
    {
        main.DrawCabinetUnavailableBanner();

        var duplicates = main.Duplicates;
        if (duplicates.MultipleCopies.Count == 0 && duplicates.ArmoireRedundant.Count == 0)
        {
            MainWindow.TextDisabledWrapped("No duplicates detected.");
            return;
        }

        if (ImGui.BeginChild("##dupelist", Vector2.Zero))
        {
            if (duplicates.ArmoireRedundant.Count > 0)
            {
                MainWindow.TextDisabledWrapped(
                    $"Already in armoire ({duplicates.ArmoireRedundant.Count}) — undyed copies you can drop:");
                foreach (var d in duplicates.ArmoireRedundant)
                    DrawDuplicateRow(d);
            }

            if (duplicates.MultipleCopies.Count > 0)
            {
                if (duplicates.ArmoireRedundant.Count > 0) ImGui.Spacing();
                MainWindow.TextDisabledWrapped(
                    $"Multiple copies in dresser ({duplicates.MultipleCopies.Count}):");
                foreach (var d in duplicates.MultipleCopies)
                    DrawDuplicateRow(d);
            }
        }
        ImGui.EndChild();
    }

    private void DrawDuplicateRow(DresserItem d)
    {
        DrawDyeSwatch(d.Stain0, d.SlotIndex * 2);
        ImGui.SameLine(0, 2);
        DrawDyeSwatch(d.Stain1, d.SlotIndex * 2 + 1);
        ImGui.SameLine();
        ImGui.TextUnformatted(main.ResolveItemName(d.ItemId));
    }

    private static void DrawDyeSwatch(byte stainId, int discriminator)
    {
        var size = new Vector2(14, 14);
        if (stainId == 0)
        {
            var pos = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            var bg = ImGui.ColorConvertFloat4ToU32(new Vector4(0.18f, 0.18f, 0.18f, 1f));
            var line = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.55f, 0.55f, 1f));
            drawList.AddRectFilled(pos, pos + size, bg);
            drawList.AddRect(pos, pos + size, line);
            drawList.AddLine(pos, new Vector2(pos.X + size.X, pos.Y + size.Y), line, 1.2f);
            ImGui.InvisibleButton($"##nodye{discriminator}", size);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Undyed");
            return;
        }
        var sheet = Service.DataManager.GetExcelSheet<Stain>();
        if (sheet is null) { ImGui.Dummy(size); return; }
        var row = sheet.GetRowOrDefault(stainId);
        if (row is null) { ImGui.Dummy(size); return; }

        var color = row.Value.Color;
        var v4 = new Vector4(
            ((color >> 16) & 0xFF) / 255f,
            ((color >> 8) & 0xFF) / 255f,
            (color & 0xFF) / 255f,
            1f);
        ImGui.ColorButton(
            $"##stain{stainId}_{discriminator}", v4,
            ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoBorder,
            size);
        if (ImGui.IsItemHovered())
        {
            var name = row.Value.Name.ExtractText();
            if (!string.IsNullOrWhiteSpace(name)) ImGui.SetTooltip(name);
        }
    }
}
