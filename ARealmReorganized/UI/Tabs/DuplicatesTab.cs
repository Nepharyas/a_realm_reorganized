using System.Collections.Generic;
using System.Numerics;
using ARealmReorganized.Logic;
using ARealmReorganized.Models;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
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
        main.DrawSaddlebagUnavailableBanner();

        var duplicates = main.Duplicates;
        if (duplicates.MultipleCopies.Count == 0 && duplicates.ArmoireRedundant.Count == 0)
        {
            MainWindow.TextDisabledWrapped("No duplicates detected.");
            return;
        }

        using var list = ImRaii.Child("##dupelist", Vector2.Zero);
        if (list)
        {
            DrawSection(
                "Already in the armoire", "armoiredupes", duplicates.ArmoireRedundant,
                "The armoire keeps one forever; these copies just take up space. Dyed bag or retainer copies may be worth keeping.");
            DrawSection(
                "Multiple copies", "multidupes", duplicates.MultipleCopies,
                "You own these more than once, pick which to keep.");
        }
    }

    private void DrawSection(
        string label, string sectionId, IReadOnlyList<DuplicateDetection.DuplicatedItem> items, string hint)
    {
        if (items.Count == 0) return;
        if (!ImGui.CollapsingHeader($"{label} ({items.Count})###{sectionId}", ImGuiTreeNodeFlags.DefaultOpen)) return;

        MainWindow.TextDisabledWrapped(hint);
        foreach (var item in items)
            DrawDuplicateItem(item);
        ImGui.Spacing();
    }

    private void DrawDuplicateItem(DuplicateDetection.DuplicatedItem item)
    {
        // A pair of dye swatches per dresser copy, then the name, then where the rest live.
        foreach (var dresserCopy in item.DresserCopies)
        {
            DrawDyeSwatch(dresserCopy.Stain0, dresserCopy.SlotIndex * 2);
            ImGui.SameLine(0, 2);
            DrawDyeSwatch(dresserCopy.Stain1, dresserCopy.SlotIndex * 2 + 1);
            ImGui.SameLine(0, 6);
        }
        ImGui.TextUnformatted(main.ResolveItemName(item.ItemId));
        ImGui.SameLine();
        MainWindow.TextDisabledWrapped(DescribeLocations(item));
    }

    private static string DescribeLocations(DuplicateDetection.DuplicatedItem item)
    {
        var parts = new List<string>();
        if (item.DresserCopies.Count > 0) parts.Add(CountedLabel("dresser", item.DresserCopies.Count));
        foreach (var (source, count) in item.BagCopies)
            parts.Add(CountedLabel(SourceLabel(source), count));
        foreach (var retainerCopy in item.RetainerCopies)
        {
            var name = string.IsNullOrEmpty(retainerCopy.RetainerName)
                ? $"retainer #{retainerCopy.RetainerId}"
                : retainerCopy.RetainerName;
            parts.Add(CountedLabel(name, retainerCopy.Count));
        }
        if (item.InArmoire) parts.Add("armoire");
        return string.Join(", ", parts);
    }

    private static string CountedLabel(string place, int count) =>
        count == 1 ? place : $"{place} x{count}";

    private static string SourceLabel(InventorySource source) => source switch
    {
        InventorySource.Inventory => "bags",
        InventorySource.Armoury => "armoury",
        InventorySource.Saddlebag => "saddlebag",
        _ => "retainer",
    };

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
