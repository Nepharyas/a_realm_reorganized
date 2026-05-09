using System.Collections.Generic;
using System.Numerics;
using ARealmReorganized.Logic;
using ARealmReorganized.Models;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;

namespace ARealmReorganized.UI.Tabs;

internal sealed class DuplicatesTab
{
    private readonly Plugin plugin;
    private readonly MainWindow main;
    private readonly HashSet<ushort> selectedSlots = new();

    public DuplicatesTab(Plugin plugin, MainWindow main)
    {
        this.plugin = plugin;
        this.main = main;
    }

    public string TabLabel
    {
        get
        {
            var dupeCount = main.Duplicates.MultipleCopies.Count + main.Duplicates.ArmoireRedundant.Count;
            return $"Remove duplicates ({dupeCount})###duplicates";
        }
    }

    public void Reset() => selectedSlots.Clear();

    public void Draw()
    {
        main.DrawCabinetUnavailableBanner();

        var duplicates = main.Duplicates;
        if (duplicates.MultipleCopies.Count == 0 && duplicates.ArmoireRedundant.Count == 0)
        {
            MainWindow.TextDisabledWrapped("No duplicates detected.");
            return;
        }

        if (ImGui.Button("Select duplicates (keep one of each)"))
        {
            foreach (var d in duplicates.ArmoireRedundant) selectedSlots.Add(d.SlotIndex);
            uint lastId = 0;
            var keptOne = false;
            foreach (var d in duplicates.MultipleCopies)
            {
                if (d.ItemId != lastId) { lastId = d.ItemId; keptOne = false; }
                if (!keptOne) { keptOne = true; continue; }
                selectedSlots.Add(d.SlotIndex);
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear##dupes")) selectedSlots.Clear();

        ImGui.Spacing();

        var selected = selectedSlots.Count;
        var freeSlots = InventorySpace.FreeSlots();
        var willRemove = main.ClampForApply(selected);

        MainWindow.DrawInventoryCapWarning(freeSlots, "remove", willRemove, selected, "apply");
        if (selected > 0 && !plugin.Config.DryRun && selected <= freeSlots)
            MainWindow.TextDisabledWrapped($"Inventory free: {freeSlots} slots.");

        var canApply = main.DryRunOr(plugin.Cabinet.IsFresh && plugin.Dresser.IsActivatable);
        canApply = canApply && willRemove > 0;
        ImGui.BeginDisabled(!canApply);
        if (ImGui.Button($"Apply: remove {willRemove} duplicates"))
        {
            var dryRun = plugin.Config.DryRun;
            var (newDuplicates, removed) = DuplicateDetection.Apply(
                duplicates, selectedSlots, willRemove, plugin.Executor);

            // Don't update the UI when doing a DryRun — the log opens instead.
            if (!dryRun)
            {
                main.SetDuplicates(newDuplicates);
                foreach (var slot in removed)
                    selectedSlots.Remove(slot);
            }
            plugin.SettingsWindow.OpenOnLogs();
        }
        ImGui.EndDisabled();
        ImGui.Separator();

        if (ImGui.BeginChild("##dupelist", Vector2.Zero))
        {
            if (duplicates.ArmoireRedundant.Count > 0)
            {
                MainWindow.TextDisabledWrapped(
                    $"Already in armoire ({duplicates.ArmoireRedundant.Count}) — undyed copies you can drop:");
                foreach (var d in duplicates.ArmoireRedundant)
                {
                    DrawDuplicateRow(d, "a");
                }
            }

            if (duplicates.MultipleCopies.Count > 0)
            {
                if (duplicates.ArmoireRedundant.Count > 0) ImGui.Spacing();
                MainWindow.TextDisabledWrapped(
                    $"Multiple copies in dresser ({duplicates.MultipleCopies.Count}) — pick which to keep:");
                foreach (var d in duplicates.MultipleCopies)
                {
                    DrawDuplicateRow(d, "m");
                }
            }
        }
        ImGui.EndChild();
    }

    private void DrawDuplicateRow(DresserItem d, string idPrefix)
    {
        var checkedFlag = selectedSlots.Contains(d.SlotIndex);
        if (ImGui.Checkbox($"##{idPrefix}{d.SlotIndex}", ref checkedFlag))
        {
            if (checkedFlag) selectedSlots.Add(d.SlotIndex);
            else selectedSlots.Remove(d.SlotIndex);
        }
        ImGui.SameLine();
        DrawDyeSwatch(d.Stain0, d.SlotIndex * 2);
        ImGui.SameLine(0, 2);
        DrawDyeSwatch(d.Stain1, d.SlotIndex * 2 + 1);
        ImGui.SameLine();
        var name = main.ResolveItemName(d.ItemId);
        ImGui.TextUnformatted(name);
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
