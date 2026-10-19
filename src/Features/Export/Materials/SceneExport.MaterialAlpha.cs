using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

public sealed partial class SceneExporter
{
    private static void WriteMaterialAlpha(Obj material, JadeMaterialLayer? layer, bool inverseMask)
    {
        if (layer == null) return;
        if (layer.Blend is not (0 or 7))
        {
            material["alphaMode"] = "BLEND";
            return;
        }
        if ((layer.Flags & 0x10) == 0) return;
        material["alphaMode"] = "MASK";
        material["alphaCutoff"] = inverseMask ? .5f : (layer.Flags >> 22 & 0xfc) / 255f;
    }

    private static bool CanBakeInverseMask(JadeMaterialLayer? layer) => layer != null &&
        layer.Blend is 0 or 7 && (layer.Flags & 0x90) == 0x90 &&
        ((layer.Flags & 0x400) != 0 || layer.ColorMode is 0 or 6);

    private static byte[] InverseMaskPng(ResolvedTexture texture, JadeMaterialLayer layer)
    {
        var rgba = Preview.DecodeTga(texture.ToTextureAsset());
        float factor = (layer.Flags & 0x400) != 0 ? layer.OverrideAlpha : 1;
        float threshold = (layer.Flags >> 22 & 0xfc) / 255f;
        // Jade discards alpha >= reference; glTF MASK keeps alpha >= cutoff.
        // Bake the inverse comparison including the constant alpha override.
        // Filtering the binary mask approximates the source's filtered threshold.
        for (int offset = 3; offset < rgba.Length; offset += 4)
            rgba[offset] = rgba[offset] / 255f * factor < threshold ? (byte)255 : (byte)0;
        return Png(texture.Width, texture.Height, rgba);
    }
}
