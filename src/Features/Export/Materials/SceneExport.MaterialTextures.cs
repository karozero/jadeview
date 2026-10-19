using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

public sealed partial class SceneExporter
{
    private void WriteMaterialTexture(Obj pbr, SceneObject sceneObject, JadeMaterialLayer layer, bool inverseMask)
    {
        if (layer.Texture is 0 or uint.MaxValue) return;
        if (!resolver.TryResolveTexture(layer.Texture, out var texture))
        {
            warnings.Add($"'{sceneObject.Name}' has an unresolved texture {layer.Texture:X8}.");
            return;
        }
        string key = texture.ToTextureAsset().Name;
        if (inverseMask) key += $"/inverse-mask/{layer.Flags:X8}/{layer.Custom:X4}";
        if (!imageCache.TryGetValue(key, out int image))
        {
            image = images.Count;
            byte[] png = inverseMask ? InverseMaskPng(texture, layer) : Png(texture);
            images.Add(D("name", texture.Id.ToString(), "mimeType", "image/png", "bufferView", View(png)));
            imageCache[key] = image;
        }
        int sampler = samplers.Count;
        int textureIndex = textures.Count;
        textures.Add(D("source", image, "sampler", sampler));
        pbr["baseColorTexture"] = D("index", textureIndex);
        bool linear = (layer.Flags & 4) != 0;
        samplers.Add(D("wrapS", (layer.Flags & 1) != 0 ? 10497 : 33071,
            "wrapT", (layer.Flags & 2) != 0 ? 10497 : 33071,
            "magFilter", linear ? 9729 : 9728, "minFilter", linear ? 9729 : 9728));
    }
}
