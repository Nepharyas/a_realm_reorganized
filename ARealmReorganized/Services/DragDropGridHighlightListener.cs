using System;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;

namespace ARealmReorganized.Services;

// Windows whose slots are plain drag-drop components carrying the item icon directly:
// the armoury chest, the saddlebag and the retainer grids. Each subclass only says which
// addon names it listens to and where that addon's slot array lives.
internal abstract unsafe class DragDropGridHighlightListener(
    InventoryHighlighter highlighter, params string[] addonNames)
    : AddonHighlightListener(highlighter, addonNames)
{
    protected abstract Span<Pointer<AtkComponentDragDrop>> GetSlots(AtkUnitBase* addon);

    protected override void ApplyHighlights(AtkUnitBase* addon, string addonName)
    {
        foreach (var slotPointer in GetSlots(addon))
        {
            var slotComponent = slotPointer.Value;
            if (slotComponent == null) continue;
            var ownerNode = (AtkResNode*)((AtkComponentBase*)slotComponent)->OwnerNode;
            if (ownerNode == null) continue;
            SetNodeColor(ownerNode, Highlighter.ResolveOutsideColor(ReadIconId(slotComponent)));
        }
    }

    // Read the icon id straight off the component instead of calling GetIconId(): the
    // retainer window draws while its item data is still coming in from the server, and
    // the native call dereferences the not-yet-initialized icon component during those
    // frames. A plain field read can be null-guarded.
    private static int ReadIconId(AtkComponentDragDrop* slotComponent)
    {
        var iconComponent = slotComponent->AtkComponentIcon;
        return iconComponent == null ? 0 : (int)iconComponent->IconId;
    }
}

internal sealed unsafe class ArmouryHighlightListener(InventoryHighlighter highlighter)
    : DragDropGridHighlightListener(highlighter, "ArmouryBoard")
{
    protected override Span<Pointer<AtkComponentDragDrop>> GetSlots(AtkUnitBase* addon) =>
        ((AddonArmouryBoard*)addon)->Slots;
}

internal sealed unsafe class SaddlebagHighlightListener(InventoryHighlighter highlighter)
    : DragDropGridHighlightListener(highlighter, "InventoryBuddy")
{
    protected override Span<Pointer<AtkComponentDragDrop>> GetSlots(AtkUnitBase* addon) =>
        ((AddonInventoryBuddy*)addon)->Slots;
}

// Compact mode shows a single "RetainerGrid" addon that pages via tabs, large mode shows
// five "RetainerGrid0..4" at once. Listening on all six covers both.
internal sealed unsafe class RetainerHighlightListener(InventoryHighlighter highlighter)
    : DragDropGridHighlightListener(
        highlighter,
        "RetainerGrid", "RetainerGrid0", "RetainerGrid1", "RetainerGrid2", "RetainerGrid3", "RetainerGrid4")
{
    protected override Span<Pointer<AtkComponentDragDrop>> GetSlots(AtkUnitBase* addon) =>
        ((AddonInventoryGrid*)addon)->Slots;
}
