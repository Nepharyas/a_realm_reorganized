using FFXIVClientStructs.FFXIV.Component.GUI;

namespace ARealmReorganized.Services;

// The Glamour Dresser. Its slot components aren't drag-drops (calling GetIconId on them
// crashes), and FFXIVClientStructs has no typed slot array for it, so we walk by NodeId.
// Probed against the live addon: 50 slot components at NodeIds 32..81 in display order,
// and inside each one the 40x40 item-icon image sits at inner NodeId 13. Its PartsList ->
// Parts[PartId].UldAsset -> AtkTexture.Resource carries the iconId as a plain struct
// field, no vtable call needed.
internal sealed unsafe class DresserHighlightListener(InventoryHighlighter highlighter)
    : AddonHighlightListener(highlighter, AddonName)
{
    private const string AddonName = "MiragePrismPrismBox";
    private const uint FirstSlotNodeId = 32;
    private const int VisibleSlotCount = 50;
    private const uint SlotIconImageInnerNodeId = 13;

    // NodeType values below this are primitive nodes (image, text, etc.); component
    // nodes (the slot wrappers we want) start here.
    private const int FirstComponentNodeType = 1000;

    protected override void ApplyHighlights(AtkUnitBase* addon)
    {
        for (var slotIndex = 0; slotIndex < VisibleSlotCount; slotIndex++)
        {
            var slotNode = addon->GetNodeById(FirstSlotNodeId + (uint)slotIndex);
            if (slotNode == null) continue;
            if ((int)slotNode->Type < FirstComponentNodeType) continue;
            var componentNode = (AtkComponentNode*)slotNode;
            if (componentNode->Component == null) continue;

            var iconId = ReadIconIdFromSlot(componentNode->Component);
            SetNodeColor(slotNode, Highlighter.ResolveDresserColor((int)iconId));
        }
    }

    private static uint ReadIconIdFromSlot(AtkComponentBase* slotComponent)
    {
        if (slotComponent->UldManager.NodeList == null) return 0;
        for (var i = 0; i < slotComponent->UldManager.NodeListCount; i++)
        {
            var inner = slotComponent->UldManager.NodeList[i];
            if (inner == null) continue;
            if (inner->NodeId != SlotIconImageInnerNodeId) continue;
            if (inner->Type != NodeType.Image) continue;
            return ReadIconIdFromImageNode((AtkImageNode*)inner);
        }
        return 0;
    }

    private static uint ReadIconIdFromImageNode(AtkImageNode* imageNode)
    {
        var partsList = imageNode->PartsList;
        if (partsList == null || partsList->Parts == null) return 0;
        if (imageNode->PartId >= partsList->PartCount) return 0;
        var part = &partsList->Parts[imageNode->PartId];
        if (part->UldAsset == null) return 0;
        var texture = &part->UldAsset->AtkTexture;
        if (texture->TextureType != TextureType.Resource) return 0;
        if (texture->Resource == null) return 0;
        return texture->Resource->IconId;
    }
}
