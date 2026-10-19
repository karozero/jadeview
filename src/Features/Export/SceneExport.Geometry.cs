using System.Numerics;
using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

public sealed partial class SceneExporter
{
    private int Mesh(SceneObject sceneObject, Geometry geometry, Vector3[] positions, SkinBinding[]? bindings,
        Matrix4x4? morphSpace = null)
    {
        var parts = geometry.Parts.Where((part, index) => part.VertexIndices.Length > 0 &&
            (geometry.Particles == null || index < geometry.ParticlePart)).ToList();
        if (parts.Count == 0)
            throw new InvalidDataException($"'{sceneObject.Name}' has no exportable triangles.");
        foreach (var part in parts)
            ValidatePart(sceneObject, part, positions.Length);
        var normals = AnimationMath.Normals(positions, parts);
        var uvPositions = sceneObject.Actor?.Skin(sceneObject, geometry) ?? positions;
        var influences = BuildInfluences(positions.Length, bindings);
        var morph = bindings != null ? ReadMorph(sceneObject, geometry) : null;
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
        var layers = JadeMaterialLayer.Read(material?.SourceBytes).Where(layer => !layer.Disabled).ToList();
        var baked = BakeLayeredMaterial(sceneObject, geometry, part, uvPositions, layers);
        var colorLayer = ExportColorLayer(sceneObject, layers);
        if (layers.Count > 1 && baked == null)
            warnings.Add("Multi-pass Jade materials export their primary color layer; game-specific effects are not glTF shaders.");
        if (skinned && colorLayer?.UvMode == 6)
            warnings.Add("Skinned planar UV is baked at the selected pose; animation-driven UV projection is not exported.");

        var vertexMap = new Dictionary<ExportVertexKey, uint>();
        var sourceVertices = new List<int>();
        var outputPositions = new List<float>();
        var outputNormals = new List<float>();
        var outputUvs = new List<float>();
        var indices = new uint[part.VertexIndices.Length];
        for (int corner = 0; corner < indices.Length; corner++)
        {
            int vertex = part.VertexIndices[corner];
            int uvIndex = corner < part.UvIndices.Length ? part.UvIndices[corner] : -1;
            bool validUv = uvIndex >= 0 && uvIndex < geometry.Uvs.Length;
            if (!validUv && colorLayer != null && colorLayer.UvMode != 6)
                warnings.Add($"'{sceneObject.Name}' has missing/invalid corner UVs; affected corners use (0,0).");
            var key = new ExportVertexKey(vertex, baked != null ? corner : validUv ? uvIndex : -1);
            if (!vertexMap.TryGetValue(key, out uint outputVertex))
            {
                outputVertex = checked((uint)sourceVertices.Count);
                vertexMap.Add(key, outputVertex);
                sourceVertices.Add(vertex);
                Vector3 normal = Geometry.ToRender(normals[vertex]);
                normal = normal.LengthSquared() > 1e-15f ? Vector3.Normalize(normal) : Vector3.UnitY;
                Append(outputPositions, Geometry.ToRender(positions[vertex]));
                Append(outputNormals, normal);
                Vector2 uv = validUv ? geometry.Uvs[uvIndex] : Vector2.Zero;
                uv = baked?.CornerUvs[corner] ?? ExportUv(sceneObject, uvPositions[vertex], uv, colorLayer);
                outputUvs.Add(uv.X);
                outputUvs.Add(uv.Y);
            }
            indices[corner] = outputVertex;
        }

        var attributes = D("POSITION", Floats(outputPositions.ToArray(), 3, "VEC3", true, 34962),
            "NORMAL", Floats(outputNormals.ToArray(), 3, "VEC3", false, 34962));
        // Texture embedding must not control whether mesh UVs survive export.
        if (geometry.Uvs.Length > 0 || colorLayer != null)
            attributes["TEXCOORD_0"] = Floats(outputUvs.ToArray(), 2, "VEC2", false, 34962);
        if (skinned)
            WriteInfluences(attributes, sourceVertices, influences);
        WriteVertexColors(attributes, geometry, sourceVertices, colorLayer);
        var primitive = D("attributes", attributes, "indices", Indices(indices),
            "material", baked != null ? BakedMaterial(sceneObject, material, baked) : Material(sceneObject, material, colorLayer), "mode", 4);
        if (morph != null) primitive["targets"] = MorphTargets(morph, sourceVertices, morphSpace);
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

    private static void Append(List<float> values, Vector3 value) => values.AddRange([value.X, value.Y, value.Z]);

    private int Indices(uint[] indices)
    {
        bool useShort = indices.Max() < ushort.MaxValue;
        Array values = useShort ? indices.Select(index => checked((ushort)index)).ToArray() : indices;
        var bytes = new byte[Buffer.ByteLength(values)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        accessors.Add(D("bufferView", View(bytes, 34963), "componentType", useShort ? 5123 : 5125,
            "count", indices.Length, "type", "SCALAR"));
        return accessors.Count - 1;
    }
}
