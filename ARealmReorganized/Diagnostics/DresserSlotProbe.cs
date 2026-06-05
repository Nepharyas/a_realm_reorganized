using System;
using System.Collections.Generic;
using System.IO;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace ARealmReorganized.Diagnostics;

// Probe v2: dumps the inner structure of a single dresser slot at NodeId 32 so we can
// identify the actual component type and find a safe path to read its icon id. The
// first probe established that slots live at NodeIds 32..81 with inner-count 13, but
// casting them as AtkComponentDragDrop and calling GetIconId() crashed — meaning they
// are a different component type entirely. This probe walks the slot's AtkUldManager
// (its inner node tree) and dumps every child node so we can figure out where the
// icon graphic lives and how to read its image id without a vtable mismatch.
internal sealed unsafe class DresserSlotProbe
{
    private const string AddonName = "MiragePrismPrismBox";
    private const uint TargetSlotNodeId = 32;
    private const int MaxDepth = 10;

    private readonly Plugin plugin;

    public DresserSlotProbe(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Run()
    {
        var wrapper = Service.GameGui.GetAddonByName(AddonName, 1);
        if (wrapper.Address == nint.Zero)
        {
            plugin.LogBuffer.Add("Dresser slot probe: open the Glamour Dresser first, then re-run /arr probe-dresser-slot.");
            return;
        }

        var addon = (AtkUnitBase*)wrapper.Address;
        var slotNode = addon->GetNodeById(TargetSlotNodeId);
        if (slotNode == null)
        {
            plugin.LogBuffer.Add($"Dresser slot probe: NodeId {TargetSlotNodeId} not found in the addon's node tree.");
            return;
        }

        var lines = new List<string>
        {
            $"=== {AddonName} slot probe @ {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===",
            $"Target NodeId: {TargetSlotNodeId}",
            $"Node ptr: 0x{(nint)slotNode:X}",
            $"Type: {(int)slotNode->Type} (raw enum value, ≥1000 means a component node)",
            "",
        };

        if ((int)slotNode->Type < 1000)
        {
            lines.Add("Slot is NOT a component node — bailing.");
            WriteAndLog(lines);
            return;
        }

        var componentNode = (AtkComponentNode*)slotNode;
        var component = componentNode->Component;
        if (component == null)
        {
            lines.Add("componentNode->Component is null — bailing.");
            WriteAndLog(lines);
            return;
        }

        lines.Add($"Component ptr: 0x{(nint)component:X}");
        lines.Add($"Component OwnerNode ptr: 0x{(nint)component->OwnerNode:X} (should match the slot node)");
        lines.Add("");

        DescribeUldManager(component, lines);

        lines.Add("");
        lines.Add("--- recursive walk of component's UldManager.RootNode tree ---");
        WalkTree(component->UldManager.RootNode, depth: 0, lines);

        lines.Add("");
        lines.Add("--- top-level UldManager.NodeList ---");
        for (var i = 0; i < component->UldManager.NodeListCount; i++)
        {
            var node = component->UldManager.NodeList[i];
            if (node == null) { lines.Add($"[{i:D2}] <null>"); continue; }
            lines.Add($"[{i:D2}] {DescribeNode(node)}");
        }

        WriteAndLog(lines);
    }

    private static void DescribeUldManager(AtkComponentBase* component, List<string> lines)
    {
        var manager = &component->UldManager;
        lines.Add("UldManager:");
        lines.Add($"  NodeListCount: {manager->NodeListCount}");
        lines.Add($"  RootNode ptr: 0x{(nint)manager->RootNode:X}");
        lines.Add($"  LoadedState: {(int)manager->LoadedState}");
        lines.Add($"  Objects ptr: 0x{(nint)manager->Objects:X}");
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

        // For image nodes, dump the part info — the loaded icon's image source is what
        // tells us which item icon is currently displayed.
        if (nodeType == (int)NodeType.Image)
        {
            var imageNode = (AtkImageNode*)node;
            return basics + $" image PartId={imageNode->PartId} PartsListPtr=0x{(nint)imageNode->PartsList:X}";
        }

        return basics;
    }

    private void WriteAndLog(List<string> lines)
    {
        var configDir = Service.PluginInterface.GetPluginConfigDirectory();
        Directory.CreateDirectory(configDir);
        var path = Path.Combine(configDir, "dresser-slot-probe.txt");
        File.WriteAllLines(path, lines);
        plugin.LogBuffer.Add($"Dresser slot probe written: {path} ({lines.Count} lines)");
    }
}
