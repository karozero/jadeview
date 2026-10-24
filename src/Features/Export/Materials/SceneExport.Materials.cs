using System.Numerics;
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
        var extras = D("bakedMaskedLayers", true);
        if (baked.Composition == MaterialLayerComposition.ReflectionDetail)
            extras["bakedReflectionDetail"] = true;
        materials.Add(D("name", $"{sceneObject.Name} {source?.Id}", "doubleSided", true,
            "extensions", D("KHR_materials_unlit", D()), "extras", extras,
            "pbrMetallicRoughness", D("baseColorFactor", new[] { 1f, 1f, 1f, 1f },
                "baseColorTexture", D("index", texture), "metallicFactor", 0f, "roughnessFactor", 1f)));
        return material;
    }

    private int Material(SceneObject sceneObject, ResolvedMaterial? source, JadeMaterialLayer? layer,
        int subMaterialIndex)
    {
        bool inverseMask = options.Textures && CanBakeInverseMask(layer) &&
            resolver.TryResolveTexture(layer!.Texture, out _);
        var color = ExportBaseColor(sceneObject, source, layer, subMaterialIndex);
        if (inverseMask) color[3] = 1;
        var pbr = D("baseColorFactor", color, "metallicFactor", 0f, "roughnessFactor", 1f);
        var material = D("name", $"{sceneObject.Name} {source?.Id.ToString() ?? "default"}",
            "pbrMetallicRoughness", pbr, "doubleSided", true, "extensions", D("KHR_materials_unlit", D()));
        if (SceneRenderer.IsCollision(sceneObject))
            pbr["baseColorFactor"] = new[] { .25f, .6f, 1f, 1f };
        else if (options.Textures && layer != null)
            WriteMaterialTexture(pbr, material, sceneObject, layer, inverseMask);
        WriteMaterialAlpha(material, layer, inverseMask);
        if (inverseMask) material["extras"] = D("bakedInverseAlphaTest", true);
        WarnUnsupportedBlending(sceneObject, layer, inverseMask);
        int index = materials.Count;
        materials.Add(material);
        return index;
    }

    private void WarnUnsupportedBlending(SceneObject sceneObject, JadeMaterialLayer? layer, bool inverseMask)
    {
        if (layer == null) return;
        bool customBlend = layer.Blend is not (0 or 1 or 7);
        bool inverseBlend = layer.Blend == 1 && (layer.Flags & JadeMaterialFlags.InverseBlend) != 0;
        bool combinedAlphaTest = layer.Blend is not (0 or 7) && (layer.Flags & JadeMaterialFlags.AlphaTest) != 0;
        bool inverseAlphaTest = (layer.Flags & 0x90) == 0x90 && !inverseMask;
        if (customBlend || inverseBlend || combinedAlphaTest || inverseAlphaTest)
            warnings.Add($"'{sceneObject.Name}' uses blend/alpha flags {layer.Flags:X8} that cannot be represented exactly by one glTF material; transparency is approximate.");
    }

    private float[] ExportBaseColor(SceneObject sceneObject, ResolvedMaterial? source,
        JadeMaterialLayer? layer, int subMaterialIndex)
    {
        var color = new[] { 1f, 1f, 1f, 1f };
        if (layer?.ColorMode == 2)
        {
            Vector4 constant = ExportMaterialConstant(sceneObject, source, subMaterialIndex);
            color[0] = constant.X;
            color[1] = constant.Y;
            color[2] = constant.Z;
            color[3] = constant.W;
        }
        if (layer != null && (layer.Flags & JadeMaterialFlags.OverrideAlpha) != 0)
            color[3] = layer.OverrideAlpha;
        return color;
    }

    private Vector4 ExportMaterialConstant(SceneObject sceneObject, ResolvedMaterial? source,
        int subMaterialIndex)
    {
        Vector4 original = MaterialLayerTint.ReadConstant(source);
        return options.CinematicMaterialLayers?.ApplyConstant(sceneObject.GaoKey,
            subMaterialIndex, original) ?? original;
    }
}
