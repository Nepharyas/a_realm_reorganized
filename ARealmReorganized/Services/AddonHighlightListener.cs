using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace ARealmReorganized.Services;

// Base for the per-window highlight listeners. Each one owns a game window (or a family
// of related grids), hooks its PreDraw so colors get re-applied right before the game
// draws it, and clears everything on PreFinalize so we never keep pointers into a dead
// addon.
//
// Tinting writes the node's AddRGB channels. Additive color is a separate field from the
// MultiplyRGB the game uses to grey out unequippable items, so highlighted-but-greyed
// slots keep their color. And since we tint the window's own nodes, the game handles
// z-order, clipping and tooltips for us instead of the imgui-overlay mess this replaces.
internal abstract unsafe class AddonHighlightListener : IDisposable
{
    // How hard the tint pushes the slot toward the highlight color. 1.0 washes the
    // icon out completely.
    private const float TintIntensity = 0.65f;

    private readonly HashSet<nint> markedNodes = [];

    protected InventoryHighlighter Highlighter { get; }

    protected AddonHighlightListener(InventoryHighlighter highlighter, params string[] addonNames)
    {
        Highlighter = highlighter;
        Service.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, addonNames, HandlePreDraw);
        Service.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, addonNames, HandlePreFinalize);
    }

    public void Dispose()
    {
        Service.AddonLifecycle.UnregisterListener(HandlePreDraw);
        Service.AddonLifecycle.UnregisterListener(HandlePreFinalize);
        ClearMarks();
    }

    // Walk the window's slots and call SetNodeColor on each, null for slots that
    // shouldn't be highlighted (that's what clears stale marks after a re-scan).
    protected abstract void ApplyHighlights(AtkUnitBase* addon);

    private void HandlePreDraw(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null) return;
        ApplyHighlights(addon);
    }

    // The addon is being torn down; drop its marks. Any tracked node still belongs to a
    // live addon (this handler fires before the nodes are freed), so writing to them here
    // is safe, and afterwards we hold no pointers into the dying window.
    private void HandlePreFinalize(AddonEvent type, AddonArgs args) => ClearMarks();

    protected void SetNodeColor(AtkResNode* node, Vector4? color)
    {
        if (color is null)
        {
            if (markedNodes.Remove((nint)node)) WriteAddColor(node, Vector4.Zero);
            return;
        }
        markedNodes.Add((nint)node);
        WriteAddColor(node, color.Value);
    }

    private void ClearMarks()
    {
        foreach (var nodePointer in markedNodes) WriteAddColor((AtkResNode*)nodePointer, Vector4.Zero);
        markedNodes.Clear();
    }

    private static void WriteAddColor(AtkResNode* node, Vector4 color)
    {
        node->AddRed = (short)(255 * color.X * TintIntensity);
        node->AddGreen = (short)(255 * color.Y * TintIntensity);
        node->AddBlue = (short)(255 * color.Z * TintIntensity);
    }
}
