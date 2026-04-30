using System;
using System.Collections.Generic;
using System.Numerics;
using ARealmReorganized.Logic;
using ARealmReorganized.Models;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace ARealmReorganized.UI;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    private IReadOnlyList<uint> storableCandidates = Array.Empty<uint>();
    private IReadOnlyList<SetGroup> setGroups = Array.Empty<SetGroup>();
    private bool hasScanned;

    public MainWindow(Plugin plugin) : base("A Realm Reorganized##main")
    {
        this.plugin = plugin;
        Size = new Vector2(720, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.TextWrapped(
            "Scan your Glamour Dresser for items that can be moved to the Armoire (patch 7.5+) " +
            "and detect partial sets that can be regrouped. Nothing happens until you press Apply.");
        ImGui.Separator();

        DrawServiceStatus();
        ImGui.Spacing();

        DrawOptions();
        ImGui.Spacing();

        DrawActions();
        ImGui.Separator();

        DrawResults();
    }

    private void DrawServiceStatus()
    {
        var ok = plugin.Cabinet.IsAvailable && plugin.Dresser.IsAvailable;
        if (ok)
        {
            ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), "Game services connected.");
        }
        else
        {
            ImGui.TextColored(new Vector4(1f, 0.65f, 0.2f, 1f),
                "Game services not yet wired up — Armoire/Dresser readers are stubs until Dalamud ships 7.5 support.");
        }
        ImGui.TextDisabled($"Armoire-eligible items in current game data: {plugin.Eligibility.Count}");
    }

    private void DrawOptions()
    {
        var cfg = plugin.Config;
        var changed = false;

        var dryRun = cfg.DryRun;
        if (ImGui.Checkbox("Dry run (preview only — never moves items)", ref dryRun))
        { cfg.DryRun = dryRun; changed = true; }

        var moveRelics = cfg.MoveJobRelics;
        if (ImGui.Checkbox("Move job relics to Armoire", ref moveRelics))
        { cfg.MoveJobRelics = moveRelics; changed = true; }

        var moveDungeon = cfg.MoveDungeonGear;
        if (ImGui.Checkbox("Move dungeon gear to Armoire", ref moveDungeon))
        { cfg.MoveDungeonGear = moveDungeon; changed = true; }

        var regroup = cfg.RegroupSets;
        if (ImGui.Checkbox("Detect & regroup item-series sets", ref regroup))
        { cfg.RegroupSets = regroup; changed = true; }

        var minPieces = cfg.MinPiecesForSet;
        if (ImGui.SliderInt("Min pieces to count as a set", ref minPieces, 2, 5))
        { cfg.MinPiecesForSet = minPieces; changed = true; }

        if (changed) cfg.Save();
    }

    private void DrawActions()
    {
        if (ImGui.Button("Scan"))
        {
            RunScan();
        }
        ImGui.SameLine();

        var canApply = hasScanned && !plugin.Config.DryRun && plugin.Cabinet.IsAvailable && plugin.Dresser.IsAvailable;
        ImGui.BeginDisabled(!canApply);
        if (ImGui.Button("Apply selected actions"))
        {
            // TODO: queue the executor once 7.5 game services are available.
        }
        ImGui.EndDisabled();
    }

    private void DrawResults()
    {
        if (!hasScanned)
        {
            ImGui.TextDisabled("Press Scan to populate results.");
            return;
        }

        if (ImGui.CollapsingHeader($"Storable in Armoire ({storableCandidates.Count})", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (storableCandidates.Count == 0)
                ImGui.TextDisabled("Nothing in your dresser is currently armoire-eligible.");
            else
                foreach (var id in storableCandidates)
                    ImGui.BulletText($"Item #{id}");
        }

        if (ImGui.CollapsingHeader($"Detected sets ({setGroups.Count})", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (setGroups.Count == 0)
                ImGui.TextDisabled("No multi-piece sets detected.");
            else
                foreach (var g in setGroups)
                    ImGui.BulletText($"{g.Name} — {g.Pieces.Count} pieces");
        }
    }

    private void RunScan()
    {
        var snapshot = plugin.Dresser.Snapshot();
        storableCandidates = plugin.Cabinet.ListStorable(snapshot);
        setGroups = plugin.Config.RegroupSets
            ? SetCompression.GroupBySeries(snapshot, plugin.Config.MinPiecesForSet)
            : Array.Empty<SetGroup>();
        hasScanned = true;
        Service.Log.Information(
            $"Scan: {snapshot.Count} dresser items, {storableCandidates.Count} storable, {setGroups.Count} set groups.");
    }
}
