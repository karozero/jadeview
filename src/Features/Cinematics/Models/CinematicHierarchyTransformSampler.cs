using System.Numerics;

namespace JadeView;

/// <summary>
/// Evaluates EVE transform tracks for GAOs attached to the serialized hierarchy.
/// Iris/Jaded write non-bone tracks to a hierarchy node's local matrix; the engine
/// later composes that matrix with the parent's absolute pose.
/// </summary>
internal static class CinematicHierarchyTransformSampler
{
    private const uint FlashMatrixIdentityFlag = 0x04000000;

    internal static Dictionary<uint, AnimationTrack[]> Index(IEnumerable<AnimationTrack> tracks) =>
        tracks.Where(track => track.Bone < 0 && track.Target is > 2 and < uint.MaxValue)
            .GroupBy(track => track.Target)
            .ToDictionary(group => group.Key, group => group.ToArray());

    internal static Matrix4x4 EvaluateWorldRender(SceneObject sceneObject,
        IReadOnlyDictionary<uint, AnimationTrack[]> tracksByTarget,
        float time, IReadOnlyCollection<SceneObject> worldObjects,
        AnimationTrack[]? targetTrackOverride = null, float? ancestorTime = null)
    {
        var resolving = new HashSet<SceneObject>();
        return Resolve(sceneObject, tracksByTarget, time, worldObjects,
            targetTrackOverride, ancestorTime, resolving);
    }

    private static Matrix4x4 Resolve(SceneObject sceneObject,
        IReadOnlyDictionary<uint, AnimationTrack[]> tracksByTarget,
        float time, IReadOnlyCollection<SceneObject> worldObjects,
        AnimationTrack[]? targetTrackOverride, float? ancestorTime, HashSet<SceneObject> resolving)
    {
        if (!resolving.Add(sceneObject)) return EvaluatePrimaryWorld(sceneObject,
            TracksFor(sceneObject), time);

        try
        {
            AnimationTrack[] tracks = targetTrackOverride ?? TracksFor(sceneObject);

            if (sceneObject.Actor != null)
                return sceneObject.PreviewMatrix is Matrix4x4 actorPreview
                    ? AnimationMath.ToRender(actorPreview)
                    : sceneObject.RenderMatrix;

            if (string.IsNullOrWhiteSpace(sceneObject.ParentId)
                || sceneObject.SerializedHierarchyLocalMatrix is not Matrix4x4 localJade)
                return EvaluatePrimaryWorld(sceneObject, tracks, time);

            if (!TryResolveParent(sceneObject, worldObjects, out SceneObject? parent)
                || parent is null)
                return EvaluatePrimaryWorld(sceneObject, tracks, time);

            Matrix4x4 localPoseJade = EvaluateLocal(localJade, tracks, time, sceneObject);
            Matrix4x4 parentWorld = Resolve(parent, tracksByTarget, ancestorTime ?? time,
                worldObjects, null, null, resolving);
            return AnimationMath.ToRender(localPoseJade) * parentWorld;
        }
        finally
        {
            resolving.Remove(sceneObject);
        }

        AnimationTrack[] TracksFor(SceneObject item) =>
            ReferenceEquals(item, sceneObject) && targetTrackOverride != null
                ? targetTrackOverride
                : tracksByTarget.GetValueOrDefault(item.GaoKey) ?? [];
    }

    private static Matrix4x4 EvaluatePrimaryWorld(SceneObject sceneObject,
        AnimationTrack[] tracks, float time)
    {
        Matrix4x4 sampledJade = CinematicTransformSampler.Evaluate(sceneObject.Transform, tracks, time);
        if ((sceneObject.Flags & FlashMatrixIdentityFlag) != 0)
            sampledJade = ApplyFlashReference(sampledJade,
                AnimationMath.Trs(sceneObject.InitialTransform), ResolveReferencedComponents(tracks, time));
        return AnimationMath.ToRender(sampledJade);
    }

    private static Matrix4x4 EvaluateLocal(Matrix4x4 localJade,
        AnimationTrack[] tracks, float time, SceneObject sceneObject)
    {
        if (!Matrix4x4.Decompose(localJade, out Vector3 scale, out Quaternion rotation,
                out Vector3 position))
            return CinematicTransformSampler.Evaluate(sceneObject.InitialTransform, tracks, time);

        Matrix4x4 sampledLocal = CinematicTransformSampler.Evaluate(position, rotation, scale,
            tracks, time);
        if ((sceneObject.Flags & FlashMatrixIdentityFlag) == 0)
            return sampledLocal;

        // EVE_FlashMatrix captures the current matrix when the Flash event begins.
        // For hierarchy targets this is their parent-relative local matrix.
        // Runtime script changes before capture are not serialized in BF.
        return ApplyFlashReference(sampledLocal, localJade, ResolveReferencedComponents(tracks, time));
    }

    private static Matrix4x4 ApplyFlashReference(Matrix4x4 sampledPose, Matrix4x4 flashReference,
        ReferencedComponents referencedComponents)
    {
        if (!referencedComponents.HasTransform) return sampledPose;
        if (!Matrix4x4.Decompose(sampledPose, out Vector3 scale, out Quaternion rotation,
                out Vector3 position)
            || !Matrix4x4.Decompose(flashReference, out _, out Quaternion flashRotation, out _))
            return sampledPose;

        if (referencedComponents.Position)
            position = Vector3.Transform(position, flashReference);
        if (referencedComponents.Rotation)
        {
            Matrix4x4 flashedRotation = Matrix4x4.CreateFromQuaternion(rotation)
                * Matrix4x4.CreateFromQuaternion(flashRotation);
            if (Matrix4x4.Decompose(flashedRotation, out _, out Quaternion resolvedRotation, out _))
                rotation = resolvedRotation;
        }

        return AnimationMath.Trs(new TransformData
        {
            Translation = position,
            Rotation = rotation,
            Scale = scale
        });
    }

    private static ReferencedComponents ResolveReferencedComponents(AnimationTrack[] tracks, float time)
    {
        bool hasPosition = false;
        bool hasRotation = false;
        foreach (AnimationTrack track in tracks)
        {
            if (!track.UsesFlashReference) continue;

            float localTime = time;
            if (track.CinematicPlaybackClock is { } playbackClock
                && !playbackClock.TryMap(time, out localTime))
                continue;

            if (HasComponentAtTime(track, localTime, key => key.Position != null))
                hasPosition = true;
            if (HasComponentAtTime(track, localTime, key => key.Rotation != null))
                hasRotation = true;
        }

        return new(hasPosition, hasRotation);
    }

    private static bool HasComponentAtTime(AnimationTrack track, float time,
        Func<AnimationKey, bool> hasComponent)
    {
        int keyIndex = AnimationKeySearch.LastAtOrBefore(track.Keys, time);
        while (keyIndex >= 0)
        {
            if (hasComponent(track.Keys[keyIndex])) return true;
            keyIndex--;
        }
        return false;
    }

    private readonly record struct ReferencedComponents(bool Position, bool Rotation)
    {
        internal bool HasTransform => Position || Rotation;
    }

    private static bool TryResolveParent(SceneObject child,
        IReadOnlyCollection<SceneObject> worldObjects, out SceneObject? parent)
    {
        parent = null;
        if (!uint.TryParse(child.ParentId, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out uint parentKey)) return false;
        SceneObject[] parents = worldObjects.Where(item => item.GaoKey == parentKey).Take(2).ToArray();
        if (parents.Length != 1) return false;
        parent = parents[0];
        return true;
    }
}
