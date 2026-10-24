using System.Numerics;

namespace JadeView;

internal enum CinematicHierarchyPoseStatus
{
    WorldPose,
    MissingLocalMatrix,
    ParentMissing,
    ParentAmbiguous,
    Cycle,
    AnimatedLocalPoseUnknown
}

/// <summary>
/// Resolves source-relative GAO hierarchy transforms for cinematic camera helpers.
/// Iris/Jaded compute child global = child local * parent global; the GAO primary
/// matrix is not a substitute for the serialized hierarchy local matrix.
/// </summary>
internal static class CinematicHierarchyPose
{
    internal static SceneObject[] IncludeParentChain(IEnumerable<SceneObject> dependencies,
        IReadOnlyCollection<SceneObject> objects)
    {
        var included = new HashSet<SceneObject>(dependencies);
        var pending = new Queue<SceneObject>(included);
        while (pending.TryDequeue(out var current))
        {
            if (string.IsNullOrWhiteSpace(current.ParentId)
                || !uint.TryParse(current.ParentId, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out uint parentKey)) continue;
            var parents = objects.Where(item => item.GaoKey == parentKey).Take(2).ToArray();
            if (parents.Length != 1 || !included.Add(parents[0])) continue;
            pending.Enqueue(parents[0]);
        }
        return included.ToArray();
    }

    internal static Matrix4x4 ResolveRenderMatrix(SceneObject sceneObject,
        IReadOnlyCollection<SceneObject> objects, Func<SceneObject, Matrix4x4>? readWorldPose = null)
    {
        var resolving = new HashSet<SceneObject>();
        return Resolve(sceneObject, objects, readWorldPose ?? (item => item.RenderMatrix), resolving, out _);
    }

    internal static bool TryResolveRenderMatrix(SceneObject sceneObject,
        IReadOnlyCollection<SceneObject> objects, Func<SceneObject, Matrix4x4>? readWorldPose,
        out Matrix4x4 matrix, out CinematicHierarchyPoseStatus status,
        Func<SceneObject, bool>? hasAuthoritativePreviewPose = null)
    {
        var resolving = new HashSet<SceneObject>();
        matrix = Resolve(sceneObject, objects, readWorldPose ?? (item => item.RenderMatrix),
            hasAuthoritativePreviewPose, resolving, out status);
        return status == CinematicHierarchyPoseStatus.WorldPose;
    }

    private static Matrix4x4 Resolve(SceneObject sceneObject, IReadOnlyCollection<SceneObject> objects,
        Func<SceneObject, Matrix4x4> readWorldPose, HashSet<SceneObject> resolving,
        out CinematicHierarchyPoseStatus status)
        => Resolve(sceneObject, objects, readWorldPose, null, resolving, out status);

    private static Matrix4x4 Resolve(SceneObject sceneObject, IReadOnlyCollection<SceneObject> objects,
        Func<SceneObject, Matrix4x4> readWorldPose, Func<SceneObject, bool>? hasAuthoritativePreviewPose,
        HashSet<SceneObject> resolving, out CinematicHierarchyPoseStatus status)
    {
        if (!resolving.Add(sceneObject))
        {
            status = CinematicHierarchyPoseStatus.Cycle;
            return readWorldPose(sceneObject);
        }

        try
        {
            if (string.IsNullOrWhiteSpace(sceneObject.ParentId))
            {
                status = CinematicHierarchyPoseStatus.WorldPose;
                return readWorldPose(sceneObject);
            }

            // Actor previews and hierarchy-aware EVE previews are already evaluated
            // world poses. Their local matrix has been composed with the animated
            // parent before it reaches this resolver.
            if (sceneObject.Actor != null)
            {
                status = CinematicHierarchyPoseStatus.WorldPose;
                return readWorldPose(sceneObject);
            }
            bool previewIsWorldPose = sceneObject.PreviewMatrix != null
                && (hasAuthoritativePreviewPose?.Invoke(sceneObject) ?? true);
            if (previewIsWorldPose)
            {
                status = CinematicHierarchyPoseStatus.WorldPose;
                return readWorldPose(sceneObject);
            }

            // The GAO's primary Transform is not a valid substitute for the serialized
            // hierarchy-local matrix.
            Matrix4x4? localJade = sceneObject.SerializedHierarchyLocalMatrix;
            if (localJade is not Matrix4x4 local || !IsFinite(local))
            {
                status = CinematicHierarchyPoseStatus.MissingLocalMatrix;
                return readWorldPose(sceneObject);
            }

            if (!uint.TryParse(sceneObject.ParentId, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out uint parentKey))
            {
                status = CinematicHierarchyPoseStatus.ParentMissing;
                return readWorldPose(sceneObject);
            }

            SceneObject[] parents = objects.Where(item => item.GaoKey == parentKey).ToArray();
            if (parents.Length != 1)
            {
                status = parents.Length == 0
                    ? CinematicHierarchyPoseStatus.ParentMissing
                    : CinematicHierarchyPoseStatus.ParentAmbiguous;
                return readWorldPose(sceneObject);
            }

            Matrix4x4 parentPose = Resolve(parents[0], objects, readWorldPose,
                hasAuthoritativePreviewPose, resolving, out status);
            if (status != CinematicHierarchyPoseStatus.WorldPose) return readWorldPose(sceneObject);

            Matrix4x4 localRender = AnimationMath.ToRender(local);
            return localRender * parentPose;
        }
        finally
        {
            resolving.Remove(sceneObject);
        }
    }

    private static bool IsFinite(Matrix4x4 matrix) =>
        float.IsFinite(matrix.M11) && float.IsFinite(matrix.M12) && float.IsFinite(matrix.M13) && float.IsFinite(matrix.M14)
        && float.IsFinite(matrix.M21) && float.IsFinite(matrix.M22) && float.IsFinite(matrix.M23) && float.IsFinite(matrix.M24)
        && float.IsFinite(matrix.M31) && float.IsFinite(matrix.M32) && float.IsFinite(matrix.M33) && float.IsFinite(matrix.M34)
        && float.IsFinite(matrix.M41) && float.IsFinite(matrix.M42) && float.IsFinite(matrix.M43) && float.IsFinite(matrix.M44);
}
