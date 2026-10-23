using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

public sealed partial class SceneExporter
{
    private void WriteMaterialTexture(Obj pbr, Obj material, SceneObject sceneObject,
        JadeMaterialLayer layer, bool inverseMask)
    {
        if (layer.Texture is 0 or uint.MaxValue) return;
        if (!resolver.TryResolveTexture(layer.Texture, out var texture))
        {
            if (resolver.ProceduralTextures.TryGetValue(layer.Texture, out var procedural))
            {
                WriteProceduralTextureMetadata(material, procedural);
                string media = procedural.MediaKey is uint mediaKey
                    ? $"media {procedural.MediaName ?? "unresolved"} ({mediaKey:X8})"
                    : "no serialized media key";
                warnings.Add($"'{sceneObject.Name}' uses {ProceduralTextureKind.Name(procedural.Subtype)} "
                    + $"texture {procedural.Key:X8} ({procedural.Width}x{procedural.Height}, {media}); "
                    + "GLB stores source metadata but cannot play the procedural texture.");
                return;
            }

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

    private static void WriteProceduralTextureMetadata(Obj material, ResolvedProceduralTexture texture)
    {
        var extras = material.TryGetValue("extras", out var existing) && existing is Obj values
            ? values : new Obj();
        extras["jadeProceduralTexture"] = D(
            "key", $"{texture.Key:X8}",
            "subtype", texture.Subtype,
            "kind", ProceduralTextureKind.Name(texture.Subtype),
            "width", texture.Width,
            "height", texture.Height,
            "mediaKey", texture.MediaKey is uint mediaKey ? $"{mediaKey:X8}" : "unresolved",
            "mediaName", texture.MediaName ?? "unresolved",
            "scope", texture.Scope);
        material["extras"] = extras;
    }
}
