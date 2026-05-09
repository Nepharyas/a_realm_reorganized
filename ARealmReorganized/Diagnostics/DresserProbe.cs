using System;
using System.Collections.Generic;
using System.IO;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace ARealmReorganized.Diagnostics;

// One-shot diagnostic that dumps the MiragePrismPrismBox addon's node tree to a file in
// the plugin's config directory. The dresser's slot-component layout isn't typed in
// FFXIVClientStructs; this dump is what we need to figure out where the per-slot
// components live so the highlighter can tint them. To be removed once the dresser walk
// is wired up against verified node ids.
internal sealed unsafe class DresserProbe
{
    private const string AddonName = "MiragePrismPrismBox";
    private const int MaxDepth = 14;

    private readonly Plugin plugin;

    public DresserProbe(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Run()
    {
        var wrapper = Service.GameGui.GetAddonByName(AddonName, 1);
        if (wrapper.Address == nint.Zero)
        {
            plugin.LogBuffer.Add("Dresser probe: open the Glamour Dresser first, then re-run /arr probe-dresser.");
            return;
        }

        var addon = (AtkUnitBase*)wrapper.Address;
        var lines = new List<string>
        {
            $"=== {AddonName} probe @ {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===",
            $"Addon ptr: 0x{(nint)addon:X}",
            $"IsVisible: {addon->IsVisible}",
            $"NodeListCount: {addon->UldManager.NodeListCount}",
            "",
            "--- top-level UldManager.NodeList ---",
        };

        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node == null) { lines.Add($"[{i:D3}] <null>"); continue; }
            lines.Add($"[{i:D3}] {DescribeNode(node)}");
        }

        lines.Add("");
        lines.Add("--- recursive walk from RootNode ---");
        WalkTree(addon->RootNode, depth: 0, lines);

        var configDir = Service.PluginInterface.GetPluginConfigDirectory();
        Directory.CreateDirectory(configDir);
        var path = Path.Combine(configDir, "dresser-probe.txt");
        File.WriteAllLines(path, lines);

        plugin.LogBuffer.Add($"Dresser probe written: {path} ({lines.Count} lines)");
    }

    private static void WalkTree(AtkResNode* node, int depth, List<string> lines)
    {
        while (node != null)
        {
            var indent = new string(' ', depth * 2);
            lines.Add($"{indent}{DescribeNode(node)}");

            if (depth >= MaxDepth)
            {
                lines.Add($"{indent}  ... (max depth {MaxDepth} reached)");
            }
            else
            {
                if ((int)node->Type >= 1000)
                {
                    var componentNode = (AtkComponentNode*)node;
                    if (componentNode->Component != null)
                    {
                        var componentRoot = componentNode->Component->UldManager.RootNode;
                        if (componentRoot != null)
                        {
                            lines.Add($"{indent}  {{component children}}");
                            WalkTree(componentRoot, depth + 1, lines);
                        }
                    }
                }
                if (node->ChildNode != null) WalkTree(node->ChildNode, depth + 1, lines);
            }

            node = node->NextSiblingNode;
        }
    }

    private static string DescribeNode(AtkResNode* node)
    {
        var nodeType = (int)node->Type;
        var basics =
            $"id={node->NodeId,-5} type={nodeType,-5} pos=({node->X,5:F0},{node->Y,5:F0}) " +
            $"size=({node->Width,3}x{node->Height,3}) visible={node->IsVisible()}";

        if (nodeType < 1000) return basics;

        var componentNode = (AtkComponentNode*)node;
        if (componentNode->Component == null) return basics + " comp=<null>";

        var inner = componentNode->Component->UldManager.NodeListCount;
        return basics + $" comp_inner_count={inner}";
    }
}
