using System;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Enums;
using KamiToolKit.Nodes.Simplified;

namespace ARealmReorganized.Services;

// One highlight ring for one slot. Item icons already come with a soft glow texture for
// their hover state (IconA_Frame part 16); we draw our own copy of it right next to the
// game's hidden one, so it sits in the same spot and is clipped the same way, and it never
// touches the icon underneath. The colour goes on the node itself, so the item's rarity
// background can't bleed into it.
internal sealed unsafe class SlotOutline : IDisposable
{
    internal const int GlowSize = 72;

    private const string FrameTexturePath = "ui/uld/IconA_Frame.tex";
    private static readonly Vector2 GlowTextureOrigin = new(240, 0);

    // The game stores multiply colour as a percentage.
    private const float MultiplyScale = 100f;

    private readonly SimpleImageNode node;

    public SlotOutline(AtkResNode* gameGlow)
    {
        node = new SimpleImageNode
        {
            TexturePath = FrameTexturePath,
            TextureCoordinates = GlowTextureOrigin,
            TextureSize = new Vector2(GlowSize, GlowSize),
            Size = new Vector2(gameGlow->Width, gameGlow->Height),
            Position = new Vector2(gameGlow->X, gameGlow->Y),
            NodeFlags = NodeFlags.Visible | NodeFlags.Enabled | NodeFlags.AnchorTop | NodeFlags.AnchorLeft,
            Alpha = 1f,
        };
        try
        {
            node.AttachNode(gameGlow, NodePosition.AfterTarget);
        }
        catch
        {
            // Don't leave a loose node in the window if it couldn't be linked in.
            node.Dispose();
            throw;
        }
    }

    // Multiply paints the (near white) glow in the colour.
    public void Show(Vector4 color)
    {
        AtkResNode* resNode = node;
        resNode->MultiplyRed = (byte)(color.X * MultiplyScale);
        resNode->MultiplyGreen = (byte)(color.Y * MultiplyScale);
        resNode->MultiplyBlue = (byte)(color.Z * MultiplyScale);
        node.IsVisible = true;
    }

    public void Hide() => node.IsVisible = false;

    public void Dispose() => node.Dispose();
}
