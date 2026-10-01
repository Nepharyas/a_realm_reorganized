using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace ARealmReorganized.Services;

// Base for the per-window highlight listeners. Each one owns a game window (or a family
// of related grids), hooks its PreDraw so the outlines get refreshed right before the
// game draws it, and drops everything on PreFinalize so we never keep nodes inside a
// dead addon.
//
// Each highlighted slot gets its own SlotOutline node added to the window. Since it's a
// node of ours inside the game's own tree, the game handles z-order, clipping and
// tooltips for us, and the colour never mixes with the icon the way tinting the slot did.
internal abstract unsafe class AddonHighlightListener : IDisposable
{
    // NodeType values below this are primitive nodes (image, text, etc.); component
    // nodes start here.
    protected const int FirstComponentNodeType = 1000;

    // One outline per slot component, created the first time the slot needs one and kept
    // (hidden) until the window goes away.
    private readonly Dictionary<nint, SlotOutline> outlines = [];
    private bool creationFailureLogged;

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

    // Walk the window's slots and call SetSlotColor on each, null for slots that
    // shouldn't be highlighted (that's what hides stale outlines after a re-scan).
    protected abstract void ApplyHighlights(AtkUnitBase* addon, string addonName);

    private void HandlePreDraw(AddonEvent type, AddonArgs args)
    {
        if (!Highlighter.OutlinesReady) return;
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null) return;
        ApplyHighlights(addon, args.AddonName);
    }

    // The addon is being torn down; free our nodes while it's still alive (this handler
    // fires before the game frees its own nodes), so afterwards we hold nothing that
    // points into the dying window.
    private void HandlePreFinalize(AddonEvent type, AddonArgs args) => ClearMarks();

    // Runs whenever marks are dropped (addon teardown or dispose); listeners that cache
    // pointers of their own hang their cleanup here.
    protected virtual void OnMarksCleared()
    {
    }

    // The component is the slot itself; its node list (or that of the icon component
    // nested in it) holds the glow image the outline sits next to.
    protected void SetSlotColor(AtkComponentBase* slotComponent, Vector4? color)
    {
        if (slotComponent == null) return;
        var slotKey = (nint)slotComponent;

        if (color is null)
        {
            if (outlines.TryGetValue(slotKey, out var existing)) existing.Hide();
            return;
        }

        if (!outlines.TryGetValue(slotKey, out var outline))
        {
            var gameGlow = FindGlowNode(slotComponent);
            if (gameGlow == null) return;
            outline = TryCreateOutline(gameGlow);
            if (outline is null) return;
            outlines[slotKey] = outline;
        }
        outline.Show(color.Value);
    }

    // A window that refuses our node would otherwise throw on every draw; log it once and
    // leave that slot alone.
    private SlotOutline? TryCreateOutline(AtkResNode* gameGlow)
    {
        try
        {
            return new SlotOutline(gameGlow);
        }
        catch (Exception exception)
        {
            if (!creationFailureLogged) Service.Log.Error(exception, "Couldn't add a slot outline, skipping.");
            creationFailureLogged = true;
            return null;
        }
    }

    private void ClearMarks()
    {
        foreach (var outline in outlines.Values) outline.Dispose();
        outlines.Clear();
        OnMarksCleared();
    }

    // The game's own hover glow: the one 72x72 image inside a slot. Found by shape rather
    // than by id, since the dresser slots and the drag-drop slots number theirs
    // differently, and in drag-drop slots it lives one component deeper.
    private static AtkResNode* FindGlowNode(AtkComponentBase* component)
    {
        var nodeList = component->UldManager.NodeList;
        if (nodeList == null) return null;
        for (var i = 0; i < component->UldManager.NodeListCount; i++)
        {
            var node = nodeList[i];
            if (node == null) continue;
            if (node->Type == NodeType.Image && node->Width == SlotOutline.GlowSize && node->Height == SlotOutline.GlowSize)
            {
                return node;
            }
            if ((int)node->Type < FirstComponentNodeType) continue;
            var nested = ((AtkComponentNode*)node)->Component;
            if (nested == null) continue;
            var found = FindGlowNode(nested);
            if (found != null) return found;
        }
        return null;
    }
}
