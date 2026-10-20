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
            && sceneObject.Actor?.Root.IsControlHidden != true && (sceneObject.DrawFlags & 8) != 0;
        return options.GameHidden || visibleInPreview;
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
        int node = Node(sceneObject, matrix, animatedCinematicObjects.Contains(sceneObject));
        Add(node, children);
        if (KeepGeometryCarrier(node, geometry)) return;
        if (geometry.Billboards.Count > 0) warnings.Add("SPG vegetation exports as fixed-facing quads; camera billboarding and wind are viewport effects.");
        var posed = sceneObject.Actor?.Skin(sceneObject, geometry) ?? geometry.Positions;
        nodes[node]["mesh"] = Mesh(sceneObject, geometry, posed, null);
    }
    private void Add(int node, List<int>? children)
    {
        if (children == null) roots.Add(node);
        else children.Add(node);
    }

}
