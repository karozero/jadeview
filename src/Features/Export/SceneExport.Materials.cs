using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

public sealed partial class SceneExporter
{
    private int BakedMaterial(SceneObject sceneObject, ResolvedMaterial? source, BakedMaterialTexture baked)
    {
        int image = images.Count;
        images.Add(D("name", $"{sceneObject.Name} masked layers", "mimeType", "image/png",
            "bufferView", View(Png(baked.Size, baked.Size, baked.Rgba))));
        int texture = textures.Count;
        textures.Add(D("source", image, "sampler", samplers.Count));
        samplers.Add(D("wrapS", 33071, "wrapT", 33071, "magFilter", 9729, "minFilter", 9729));
        int material = materials.Count;
        materials.Add(D("name", $"{sceneObject.Name} {source?.Id}", "doubleSided", true,
            "extensions", D("KHR_materials_unlit", D()), "extras", D("bakedMaskedLayers", true),
            "pbrMetallicRoughness", D("baseColorFactor", new[] { 1f, 1f, 1f, 1f },
                "baseColorTexture", D("index", texture), "metallicFactor", 0f, "roughnessFactor", 1f)));
        return material;
    }

    private int Material(SceneObject sceneObject, ResolvedMaterial? source, JadeMaterialLayer? layer)
    {
        var pbr = D("baseColorFactor", ExportBaseColor(source, layer), "metallicFactor", 0f, "roughnessFactor", 1f);
        var material = D("name", $"{sceneObject.Name} {source?.Id.ToString() ?? "default"}",
            "pbrMetallicRoughness", pbr, "doubleSided", true, "extensions", D("KHR_materials_unlit", D()));
        if (SceneRenderer.IsCollision(sceneObject))
            pbr["baseColorFactor"] = new[] { .25f, .6f, 1f, 1f };
        else if (options.Textures && layer != null)
            WriteMaterialTexture(pbr, sceneObject, layer);
        WriteMaterialAlpha(material, layer);
        WarnUnsupportedBlending(sceneObject, layer);
        int index = materials.Count;
        materials.Add(material);
        return index;
    }

    private void WarnUnsupportedBlending(SceneObject sceneObject, JadeMaterialLayer? layer)
    {
        if (layer == null) return;
        bool customBlend = layer.Blend is not (0 or 1 or 7);
        bool inverseBlend = layer.Blend == 1 && (layer.Flags & 0x80) != 0;
        bool combinedAlphaTest = layer.Blend is not (0 or 7) && (layer.Flags & 0x10) != 0;
        bool inverseAlphaTest = (layer.Flags & 0x90) == 0x90;
        if (customBlend || inverseBlend || combinedAlphaTest || inverseAlphaTest)
            warnings.Add($"'{sceneObject.Name}' uses blend/alpha flags {layer.Flags:X8} that cannot be represented exactly by one glTF material; transparency is approximate.");
    }

    private void WriteMaterialTexture(Obj pbr, SceneObject sceneObject, JadeMaterialLayer layer)
    {
        if (layer.Texture is 0 or uint.MaxValue) return;
        if (!resolver.TryResolveTexture(layer.Texture, out var texture))
        {
            warnings.Add($"'{sceneObject.Name}' has an unresolved texture {layer.Texture:X8}.");
            return;
        }
        string key = texture.ToTextureAsset().Name;
        if (!imageCache.TryGetValue(key, out int image))
        {
            image = images.Count;
            images.Add(D("name", texture.Id.ToString(), "mimeType", "image/png", "bufferView", View(Png(texture))));
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

    private static void WriteMaterialAlpha(Obj material, JadeMaterialLayer? layer)
    {
        if (layer == null) return;
        if (layer.Blend is not (0 or 7))
        {
            material["alphaMode"] = "BLEND";
            return;
        }
        if ((layer.Flags & 0x10) == 0) return;
        material["alphaMode"] = "MASK";
        material["alphaCutoff"] = (layer.Flags >> 22 & 0xfc) / 255f;
    }
    private static float[] ExportBaseColor(ResolvedMaterial? source, JadeMaterialLayer? layer)
    {
        var color = new[] { 1f, 1f, 1f, 1f };
        if (layer?.ColorMode == 2 && source?.SourceBytes is { Length: >= 16 } bytes)
        {
            uint packed = Preview.U32(bytes, 12);
            color[0] = (packed & 255) / 255f;
            color[1] = (packed >> 8 & 255) / 255f;
            color[2] = (packed >> 16 & 255) / 255f;
        }
        if (layer != null && (layer.Flags & 0x400) != 0)
            color[3] = layer.OverrideAlpha;
        return color;
    }
}
