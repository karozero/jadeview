using System.Numerics;

namespace JadeView;

public sealed partial class SceneExporter
{
    private BakedMaterialTexture? BakeLayeredMaterial(SceneObject sceneObject, Geometry geometry,
        Geometry.Part part, Vector3[] positions, List<JadeMaterialLayer> layers)
    {
        var composition = MaterialLayerRules.Composition(layers);
        bool canBake = options.Textures && composition != MaterialLayerComposition.Unsupported && sceneObject.RliColors.Length == 0;
        if (!canBake || !CanBakeLayerUvs(sceneObject, geometry, part, layers)) return null;
        var sources = ReadBakeSources(layers);
        if (sources == null) return null;
        float alphaScale = (layers[1].Flags & 0x400) != 0 ? layers[1].OverrideAlpha : 1;
        var baked = MaterialAtlasBaker.Bake(part.VertexIndices.Length / 3, sources,
            triangle => LayerCornerUvs(sceneObject, geometry, part, positions, layers, triangle), alphaScale, composition);
        if (baked != null)
            warnings.Add($"'{sceneObject.Name}' masked material layers are baked into a triangle atlas at the selected pose; animated UV projection remains frozen.");
        return baked;
    }

    private bool CanBakeLayerUvs(SceneObject sceneObject, Geometry geometry, Geometry.Part part,
        IReadOnlyList<JadeMaterialLayer> layers)
    {
        bool needsStoredUvs = layers.Any(layer => layer.UvMode == 0);
        bool validStoredUvs = part.UvIndices.Length == part.VertexIndices.Length &&
            part.UvIndices.All(index => index >= 0 && index < geometry.Uvs.Length);
        if (needsStoredUvs && !validStoredUvs) return false;
        foreach (var layer in layers.Where(layer => layer.UvMode == 6))
        {
            int space = layer.Custom >> 6 & 3;
            bool movingProjection = space is 1 or 2;
            bool missingBone = space == 3 && !TryBoneProjection(sceneObject, layer, out _);
            if (movingProjection || missingBone) return false;
        }
        return true;
    }

    private List<BakeTextureSource>? ReadBakeSources(IReadOnlyList<JadeMaterialLayer> layers)
    {
        var sources = new List<BakeTextureSource>();
        foreach (var layer in layers)
        {
            if (!resolver.TryResolveTexture(layer.Texture, out var texture)) return null;
            bool validSize = texture.Width > 0 && texture.Height > 0;
            if (!validSize) return null;
            sources.Add(new(texture.Width, texture.Height, Preview.DecodeTga(texture.ToTextureAsset()), layer.Flags));
        }
        return sources;
    }

    private Vector2[][] LayerCornerUvs(SceneObject sceneObject, Geometry geometry, Geometry.Part part,
        Vector3[] positions, List<JadeMaterialLayer> layers, int triangle)
    {
        return layers.Select(layer => Enumerable.Range(triangle * 3, 3).Select(corner =>
        {
            int uvIndex = corner < part.UvIndices.Length ? part.UvIndices[corner] : -1;
            var uv = uvIndex >= 0 && uvIndex < geometry.Uvs.Length ? geometry.Uvs[uvIndex] : Vector2.Zero;
            return ExportUv(sceneObject, positions[part.VertexIndices[corner]], uv, layer);
        }).ToArray()).ToArray();
    }

}
