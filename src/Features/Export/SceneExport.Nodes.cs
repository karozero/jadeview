using System.Numerics;
using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

public sealed partial class SceneExporter
{
    private Matrix4x4 Placement(SceneObject sceneObject)
    {
        var matrix = options.Placement == ExportPlacement.Original ? sceneObject.InitialTransform.ToRenderMatrix() : sceneObject.RenderMatrix;
        if (options.Placement == ExportPlacement.AtOrigin) matrix *= originOffset;
        return matrix;
    }
    private bool Included(SceneObject sceneObject)
    {
        if (SceneRenderer.IsCollision(sceneObject)) return options.Collisions;
        bool visibleInPreview = !sceneObject.PreviewHidden && !sceneObject.IsControlHidden
            && sceneObject.Actor?.Root.IsControlHidden != true
            && (sceneObject.EffectiveDrawFlags & SceneObject.DrawVisibilityMask) != 0;
        bool hasCinematicMorph = options.CinematicMorphTimelines?.ContainsKey(sceneObject) == true;
        return options.GameHidden || visibleInPreview
            || options.Animations && (cinematicVisibilityObjects.Contains(sceneObject) || hasCinematicMorph);
    }
    private int Node(SceneObject sceneObject, Matrix4x4 matrix, bool trs = false)
    {
        var nodeData = D("name", sceneObject.Name, "extras", D(
            "gaoKey", sceneObject.GaoKey.ToString("X8"),
            "groKey", sceneObject.GeometryKey.ToString("X8"),
            "grmKey", sceneObject.MaterialKey.ToString("X8"),
            "collision", SceneRenderer.IsCollision(sceneObject),
            "sourceId", sceneObject.Id, "flags", sceneObject.Flags, "controlFlags", sceneObject.ControlFlags,
            "recordOffset", sceneObject.RecordRange.Offset, "recordSize", sceneObject.RecordRange.Size));
        if (trs) SetTrs(nodeData, matrix);
        else nodeData["matrix"] = Matrix(matrix);
        if (!sourceCameraObjects.Contains(sceneObject) && animatedCinematicObjects.Contains(sceneObject)
              && sceneObject.Name.Contains("camera", StringComparison.OrdinalIgnoreCase))
        {
            nodeData["camera"] = cameras.Count;
            cameras.Add(CameraDefinition(sceneObject));
        }
        nodes.Add(nodeData);
        int nodeIndex = nodes.Count - 1;
        nodeByObject.TryAdd(sceneObject, nodeIndex);
        return nodeIndex;
    }
    private static float[] Matrix(Matrix4x4 matrix) => [matrix.M11, matrix.M12, matrix.M13, 0, matrix.M21, matrix.M22, matrix.M23, 0, matrix.M31, matrix.M32, matrix.M33, 0, matrix.M41, matrix.M42, matrix.M43, 1];
    private static (Vector3 Scale, Quaternion Rotation, Vector3 Position) Decompose(Matrix4x4 matrix)
    {
        if (!Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var position)) throw new InvalidDataException("Animation matrix cannot be decomposed into glTF transforms.");
        return (scale, Quaternion.Normalize(rotation), position);
    }
    private static void SetTrs(Obj nodeData, Matrix4x4 matrix)
    {
        var (scale, rotation, translation) = Decompose(matrix);
        nodeData["translation"] = new[] { translation.X, translation.Y, translation.Z };
        nodeData["rotation"] = new[] { rotation.X, rotation.Y, rotation.Z, rotation.W };
        nodeData["scale"] = new[] { scale.X, scale.Y, scale.Z };
    }
    private void Static(SceneObject sceneObject, List<int>? children, Matrix4x4 matrix)
    {
        if (!Included(sceneObject)) return;
        if (!resolver.Geometries.TryGetValue(sceneObject.RenderGeometryKey, out var geometry))
        {
            if (!options.UnknownObjects && !animatedCinematicObjects.Contains(sceneObject)) return;
            int empty = Node(sceneObject, matrix, animatedCinematicObjects.Contains(sceneObject));
            ((Obj)nodes[empty]["extras"])["unresolvedGeometry"] = true;
            Add(empty, children);
            empties++;
            return;
        }
        bool hasParticleSnapshot = TryCreateParticleSnapshot(sceneObject, geometry, out var exportGeometry);
        int node = Node(sceneObject, matrix, animatedCinematicObjects.Contains(sceneObject));
        Add(node, children);
        if (hasParticleSnapshot)
        {
            var extras = (Obj)nodes[node]["extras"];
            extras["particleSnapshot"] = true;
            if (sceneObject.PreviewParticleTime is { } snapshotTime)
                extras["particleSnapshotTimeSeconds"] = snapshotTime;
            warnings.Add("PAG particles are exported as a static snapshot of the current viewport frame; emitter behavior and particle motion are not animated in GLB.");
        }
        if (KeepGeometryCarrier(node, sceneObject, exportGeometry)) return;
        if (exportGeometry.Billboards.Count > 0) warnings.Add("SPG vegetation exports as fixed-facing quads; camera billboarding and wind are viewport effects.");
        var posed = sceneObject.Actor?.Skin(sceneObject, exportGeometry) ?? exportGeometry.Positions;
        nodes[node]["mesh"] = Mesh(sceneObject, exportGeometry, posed, null);
        CaptureStaticMorphTimeline(sceneObject, node);
    }

    private static bool TryCreateParticleSnapshot(SceneObject sceneObject, Geometry source, out Geometry snapshot)
    {
        snapshot = source;
        if (source.Particles == null || sceneObject.PreviewParticlePositions is not { } positions
            || sceneObject.PreviewParticleColors is not { } colors
            || positions.Length != source.Positions.Length || colors.Length != source.VertexColors.Length
            || source.ParticleStart < 0 || source.ParticleStart > positions.Length
            || source.ParticlePart < 0 || source.ParticlePart > source.Parts.Count)
            return false;

        var parts = source.Parts.Take(source.ParticlePart).ToList();
        foreach (var sourcePart in source.Parts.Skip(source.ParticlePart))
        {
            var indices = new List<int>();
            var uvIndices = new List<int>();
            if (sourcePart.VertexIndices.Length % 6 != 0
                || sourcePart.UvIndices.Length != sourcePart.VertexIndices.Length) continue;

            for (int triangleStart = 0; triangleStart < sourcePart.VertexIndices.Length; triangleStart += 6)
            {
                if (!HasVisibleParticleVertex(sourcePart.VertexIndices, triangleStart, source.ParticleStart, colors)) continue;
                indices.AddRange(sourcePart.VertexIndices.Skip(triangleStart).Take(6));
                uvIndices.AddRange(sourcePart.UvIndices.Skip(triangleStart).Take(6));
            }
            if (indices.Count > 0)
                parts.Add(new(sourcePart.MaterialId, indices.ToArray(), uvIndices.ToArray()));
        }

        snapshot = new Geometry
        {
            Key = source.Key,
            Offset = source.Offset,
            Size = source.Size,
            MaterialKey = source.MaterialKey,
            Objects = source.Objects,
            Positions = positions,
            Uvs = source.Uvs,
            VertexColors = colors,
            Skin = source.Skin,
            Parts = parts,
            Billboards = source.Billboards
        };
        return true;
    }

    private static bool HasVisibleParticleVertex(int[] vertices, int start, int particleStart, uint[] colors)
    {
        for (int corner = 0; corner < 6; corner++)
        {
            int vertex = vertices[start + corner];
            if (vertex >= particleStart && vertex < colors.Length && (colors[vertex] >> 24) != 0)
                return true;
        }
        return false;
    }

    private void Add(int node, List<int>? children)
    {
        if (children == null) roots.Add(node);
        else children.Add(node);
    }

}
