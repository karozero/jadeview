using System.Numerics;

namespace JadeView;

public sealed partial class SceneExporter
{
    private ExportMeshVertices BuildMeshVertices(SceneObject sceneObject, Geometry geometry,
        Geometry.Part part, Vector3[] positions, Vector3[] uvPositions, Vector3[] normals,
        JadeMaterialLayer? colorLayer, BakedMaterialTexture? baked)
    {
        var vertexMap = new Dictionary<ExportVertexKey, uint>();
        var vertices = new ExportMeshVertices(part.VertexIndices.Length);
        for (int corner = 0; corner < vertices.Indices.Length; corner++)
        {
            int vertex = part.VertexIndices[corner];
            int uvIndex = corner < part.UvIndices.Length ? part.UvIndices[corner] : -1;
            bool validUv = uvIndex >= 0 && uvIndex < geometry.Uvs.Length;
            if (!validUv && colorLayer != null && colorLayer.UvMode != 6)
                warnings.Add($"'{sceneObject.Name}' has missing/invalid corner UVs; affected corners use (0,0).");
            int seamIndex = validUv ? uvIndex : -1;
            if (baked != null) seamIndex = corner;
            var key = new ExportVertexKey(vertex, seamIndex);
            if (vertexMap.TryGetValue(key, out uint existingVertex))
            {
                vertices.Indices[corner] = existingVertex;
                continue;
            }
            uint outputVertex = checked((uint)vertices.SourceVertices.Count);
            vertexMap.Add(key, outputVertex);
            vertices.SourceVertices.Add(vertex);
            Vector3 normal = Geometry.ToRender(normals[vertex]);
            normal = normal.LengthSquared() > 1e-15f ? Vector3.Normalize(normal) : Vector3.UnitY;
            Append(vertices.Positions, Geometry.ToRender(positions[vertex]));
            Append(vertices.Normals, normal);
            Vector2 uv = validUv ? geometry.Uvs[uvIndex] : Vector2.Zero;
            uv = baked?.CornerUvs[corner] ?? ExportUv(sceneObject, uvPositions[vertex], uv, colorLayer);
            vertices.Uvs.Add(uv.X);
            vertices.Uvs.Add(uv.Y);
            vertices.Indices[corner] = outputVertex;
        }

        return vertices;
    }
}
