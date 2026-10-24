using System.Numerics;

namespace JadeView;

internal sealed class CinematicEventTrackPreview
{
    private SceneObject? target;
    private AnimationTrack? track;
    private float startTime;

    internal SceneObject? Target => target;
    internal AnimationTrack? Track => track;

    internal static bool HasTransformData(AnimationTrack? sourceTrack) => sourceTrack is { Bone: < 0 }
        && sourceTrack.Keys.Any(key => HasFiniteTransform(key));

    internal bool Start(SceneObject targetObject, AnimationTrack sourceTrack, float cinematicTime)
    {
        if (!HasTransformData(sourceTrack) || !float.IsFinite(cinematicTime)) return false;
        if (target != null) target.PreviewMatrix = null;
        target = targetObject;
        track = sourceTrack;
        startTime = cinematicTime;
        return true;
    }

    internal void Evaluate(float cinematicTime, IReadOnlyCollection<SceneObject> worldObjects,
        IReadOnlyDictionary<uint, AnimationTrack[]> tracksByTarget)
    {
        if (target == null || track == null || cinematicTime < startTime) return;
        float localTime = cinematicTime - startTime;
        target.PreviewMatrix = AnimationMath.FromRender(
            CinematicHierarchyTransformSampler.EvaluateWorldRender(target, tracksByTarget,
                localTime, worldObjects, [track], ancestorTime: cinematicTime));
    }

    internal bool IsPreviewing(SceneObject targetObject, AnimationTrack sourceTrack) =>
        ReferenceEquals(target, targetObject) && ReferenceEquals(track, sourceTrack);

    private static bool HasFiniteTransform(AnimationKey key)
    {
        bool hasTransform = key.Position.HasValue || key.Rotation.HasValue || key.Scale.HasValue;
        bool positionIsFinite = key.Position is not Vector3 position || IsFinite(position);
        bool scaleIsFinite = key.Scale is not Vector3 scale || IsFinite(scale);
        bool rotationIsFinite = key.Rotation is not Quaternion rotation
            || (float.IsFinite(rotation.X) && float.IsFinite(rotation.Y) && float.IsFinite(rotation.Z)
                && float.IsFinite(rotation.W) && rotation.LengthSquared() > 1e-8f);
        return hasTransform && positionIsFinite && scaleIsFinite && rotationIsFinite;
    }

    private static bool IsFinite(Vector3 vector) => float.IsFinite(vector.X)
        && float.IsFinite(vector.Y) && float.IsFinite(vector.Z);

    internal void Clear()
    {
        if (target != null) target.PreviewMatrix = null;
        target = null;
        track = null;
        startTime = 0;
    }
}
