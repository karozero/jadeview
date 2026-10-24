using System.Numerics;

namespace JadeView;

public sealed partial class SceneExporter
{
    private BakedMaterialTexture? BakeLayeredMaterial(SceneObject sceneObject, Geometry geometry,
        Geometry.Part part, Vector3[] positions, ResolvedMaterial? material, List<JadeMaterialLayer> layers)
    {
        var composition = MaterialLayerRules.Composition(layers);
        bool reflection = composition == MaterialLayerComposition.ReflectionDetail;
        bool canBake = options.Textures && composition != MaterialLayerComposition.Unsupported;
        if (!canBake || !CanBakeLayerUvs(sceneObject, geometry, part, layers)) return null;
        var sources = ReadBakeSources(layers);
        if (sources == null) return null;
        // Per-layer tints include local-alpha overrides, so apply them only once.
        const float alphaScale = 1;
        var reflectionNormals = reflection ? ReferenceReflectionNormals(sceneObject, geometry, positions) : null;
        Func<int, Vector4[][]> tints =
            triangle => MaterialCornerTints(sceneObject, geometry, part, material, layers, triangle);
        var baked = MaterialAtlasBaker.Bake(part.VertexIndices.Length / 3, sources,
            triangle => LayerCornerUvs(sceneObject, geometry, part, positions, layers, triangle, reflectionNormals),
            alphaScale, composition, tints);
        if (baked != null)
        {
            if (reflection)
                warnings.Add($"'{sceneObject.Name}' reflection and detail layers are baked using a fixed frontal reference view; camera-dependent reflections are not animated.");
            else
                warnings.Add($"'{sceneObject.Name}' masked material layers are baked into a triangle atlas at the selected pose; animated UV projection remains frozen.");
        }
        return baked;
    }

    private Vector4[][] MaterialCornerTints(SceneObject sceneObject, Geometry geometry, Geometry.Part part,
        ResolvedMaterial? material, IReadOnlyList<JadeMaterialLayer> layers, int triangle)
    {
        int subMaterialIndex = ResolvePartSubMaterialIndex(sceneObject, part);
        Vector4 materialConstant = ExportMaterialConstant(sceneObject, material, subMaterialIndex);
        return layers.Select(layer => Enumerable.Range(triangle * 3, 3).Select(corner =>
            MaterialLayerTint.Evaluate(sceneObject, geometry, part.VertexIndices[corner], material, layer,
                ComputedExportColor(sceneObject, geometry, part.VertexIndices[corner], layer,
                    geometry.IsBillboardVertex(part.VertexIndices[0])), materialConstant)).ToArray()).ToArray();
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
        Vector3[] positions, List<JadeMaterialLayer> layers, int triangle, Vector3[]? reflectionNormals = null)
    {
        return layers.Select(layer => Enumerable.Range(triangle * 3, 3).Select(corner =>
        {
            int uvIndex = corner < part.UvIndices.Length ? part.UvIndices[corner] : -1;
            var uv = uvIndex >= 0 && uvIndex < geometry.Uvs.Length ? geometry.Uvs[uvIndex] : Vector2.Zero;
            if (layer.UvMode == 2 && reflectionNormals != null)
                return ReferenceReflectionUv(reflectionNormals[part.VertexIndices[corner]], layer, sceneObject.DrawFlags);
            return ExportUv(sceneObject, positions[part.VertexIndices[corner]], uv, layer);
        }).ToArray()).ToArray();
    }

}
