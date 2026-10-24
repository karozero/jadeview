using System.Numerics;
using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

public sealed partial class SceneExporter
{
    private bool KeepGeometryCarrier(int node, SceneObject sceneObject, Geometry geometry)
    {
        if (geometry.Particles != null)
        {
            ((Obj)nodes[node]["extras"])["particleGenerator"] = true;
            warnings.Add("PAG simulation is a viewport effect; only its carrier is exported.");
        }
        bool hasTriangles = geometry.Parts.Where((part, index) =>
            geometry.Particles == null || index < geometry.ParticlePart)
            .Any(part => part.VertexIndices.Length > 0 && HasVisibleExportLayer(sceneObject, part));
        if (hasTriangles) return false;
        empties++;
        return true;
    }

    private int Mesh(SceneObject sceneObject, Geometry geometry, Vector3[] positions, SkinBinding[]? bindings,
        Matrix4x4? morphSpace = null)
    {
        var parts = geometry.Parts.Where((part, index) => part.VertexIndices.Length > 0 &&
            (geometry.Particles == null || index < geometry.ParticlePart)
            && HasVisibleExportLayer(sceneObject, part)).ToList();
        if (parts.Count == 0)
            throw new InvalidDataException($"'{sceneObject.Name}' has no exportable triangles.");
        foreach (var part in parts)
            ValidatePart(sceneObject, part, positions.Length);
        var normals = AnimationMath.Normals(positions, parts);
        var uvPositions = sceneObject.Actor?.Skin(sceneObject, geometry) ?? positions;
        var influences = BuildInfluences(positions.Length, bindings);
        var morph = ReadMorph(sceneObject, geometry);
        var primitives = new List<Obj>();
        foreach (var part in parts)
            primitives.Add(MeshPart(sceneObject, geometry, positions, uvPositions, normals, part, influences, bindings != null,
                morph, morphSpace ?? Matrix4x4.Identity));
        int meshIndex = meshes.Count;
        var mesh = D("name", sceneObject.Name, "primitives", primitives);
        if (morph != null) WriteMorphMetadata(mesh, morph, sceneObject);
        meshes.Add(mesh);
        return meshIndex;
    }

    private static void ValidatePart(SceneObject sceneObject, Geometry.Part part, int vertexCount)
    {
        bool validTriangles = part.VertexIndices.Length % 3 == 0;
        bool validIndices = part.VertexIndices.All(index => index >= 0 && index < vertexCount);
        if (!validTriangles || !validIndices)
            throw new InvalidDataException($"Invalid triangle indices in '{sceneObject.Name}'.");
    }

    private Obj MeshPart(SceneObject sceneObject, Geometry geometry, Vector3[] positions, Vector3[] uvPositions,
        Vector3[] normals, Geometry.Part part, List<(int Joint, float Weight)>[] influences, bool skinned,
        FaceMorphData? morph, Matrix4x4 morphSpace)
    {
        var material = ResolvePartMaterial(sceneObject, part);
        int subMaterialIndex = ResolvePartSubMaterialIndex(sceneObject, part);
        var layers = ReadExportLayers(sceneObject, part, material).Where(layer => !layer.Disabled).ToList();
        var baked = BakeLayeredMaterial(sceneObject, geometry, part, uvPositions, material, layers);
        var colorLayer = baked == null ? ExportColorLayer(sceneObject, layers) : null;
        if (layers.Count > 1 && baked == null)
            warnings.Add("Multi-pass Jade materials export their primary color layer; game-specific effects are not glTF shaders.");
        if (skinned && colorLayer?.UvMode == 6)
            warnings.Add("Skinned planar UV is baked at the selected pose; animation-driven UV projection is not exported.");

        var vertices = BuildMeshVertices(sceneObject, geometry, part, positions, uvPositions, normals, colorLayer, baked);
        var attributes = D("POSITION", Floats(vertices.Positions.ToArray(), 3, "VEC3", true, GlbFormat.VertexBuffer),
            "NORMAL", Floats(vertices.Normals.ToArray(), 3, "VEC3", false, GlbFormat.VertexBuffer));
        // Texture embedding must not control whether mesh UVs survive export.
        if (geometry.Uvs.Length > 0 || colorLayer != null)
            attributes["TEXCOORD_0"] = Floats(vertices.Uvs.ToArray(), 2, "VEC2", false, GlbFormat.VertexBuffer);
        if (skinned)
            WriteInfluences(attributes, vertices.SourceVertices, influences);
        if (baked == null) WriteVertexColors(attributes, sceneObject, geometry, vertices.SourceVertices, colorLayer, part);
        var primitive = D("attributes", attributes, "indices", Indices(vertices.Indices),
            "material", baked != null ? BakedMaterial(sceneObject, material, baked)
                : Material(sceneObject, material, colorLayer, subMaterialIndex), "mode", GlbFormat.Triangles);
        if (morph != null) primitive["targets"] = MorphTargets(morph, vertices.SourceVertices, morphSpace);
        return primitive;
    }

    private ResolvedMaterial? ResolvePartMaterial(SceneObject sceneObject, Geometry.Part part)
    {
        var material = resolver.Materials.GetValueOrDefault(sceneObject.MaterialKey);
        if (material?.Type != 4 || material.SubMaterialKeys.Count == 0)
            return material;
        int index = Math.Clamp(part.MaterialId, 0, material.SubMaterialKeys.Count - 1);
        return resolver.Materials.GetValueOrDefault(material.SubMaterialKeys[index]);
    }

    private int ResolvePartSubMaterialIndex(SceneObject sceneObject, Geometry.Part part)
    {
        var root = resolver.Materials.GetValueOrDefault(sceneObject.MaterialKey);
        return root is { Type: 4, SubMaterialKeys.Count: > 0 }
            ? Math.Clamp(part.MaterialId, 0, root.SubMaterialKeys.Count - 1) : 0;
    }

    private bool HasVisibleExportLayer(SceneObject sceneObject, Geometry.Part part)
    {
        if (options.CinematicMaterialLayers == null) return true;
        var material = ResolvePartMaterial(sceneObject, part);
        var layers = ReadExportLayers(sceneObject, part, material);
        return layers.Count == 0 || layers.Any(layer => !layer.Disabled);
    }

    private IReadOnlyList<JadeMaterialLayer> ReadExportLayers(SceneObject sceneObject,
        Geometry.Part part, ResolvedMaterial? material)
    {
        var layers = JadeMaterialLayer.Read(material?.SourceBytes);
        if (options.CinematicMaterialLayers is not { } snapshot) return layers;
        int subMaterial = ResolvePartSubMaterialIndex(sceneObject, part);
        return layers.Select((layer, level) => snapshot.Apply(material?.Id.Key ?? 0,
            sceneObject.GaoKey, subMaterial, level, layer)).ToArray();
    }

    private static void Append(List<float> values, Vector3 value) => values.AddRange([value.X, value.Y, value.Z]);

}
