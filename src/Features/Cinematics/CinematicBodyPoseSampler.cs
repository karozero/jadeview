using System.Numerics;

namespace JadeView;

internal sealed class CinematicBodyPoseSampler(SceneActor actor)
{
    private CinematicBodyTimeline? timeline;
    private Dictionary<SceneObject, Matrix4x4> baseline = [];
    private readonly Dictionary<SceneObject, Matrix4x4> sampledPose = [];
    private Dictionary<SceneObject, Matrix4x4> stockPoseA = [];
    private Dictionary<SceneObject, Matrix4x4> stockPoseB = [];
    private readonly SceneObject[] uniqueBones = actor.Bones.OfType<SceneObject>().Distinct().ToArray();
    private int cachedSegment = -1;
    private int cachedFrame = -1;
    internal int BaselineRefreshCount { get; private set; }

    internal Dictionary<SceneObject, Matrix4x4> Evaluate(CinematicBodyTimeline body, float time,
        Dictionary<SceneObject, Matrix4x4> initial)
    {
        bool changed = timeline != body || baseline.Count != initial.Count
            || initial.Any(pair => !baseline.TryGetValue(pair.Key, out var previous) || previous != pair.Value);
        if (changed)
        {
            BaselineRefreshCount++;
            timeline = body;
            baseline = new(initial);
            cachedSegment = -1;
            cachedFrame = -1;
        }

        if (body.BlendAt(time, int.MaxValue) is null)
        {
            ApplyLeadPose(initial, time, int.MaxValue);
            return initial;
        }

        return PoseAt(time, int.MaxValue);
    }

    private Dictionary<SceneObject, Matrix4x4> PoseAt(float time, int maxSegment)
    {
        if (timeline!.BlendAt(time, maxSegment) is not { } blend)
        {
            CopyPose(baseline, sampledPose);
            ApplyLeadPose(sampledPose, time, maxSegment);
            return sampledPose;
        }
        float frames = (blend.Time - blend.Start) * 60;
        int firstFrame = (int)MathF.Floor(frames + .0001f);
        float amount = Math.Clamp(frames - firstFrame, 0, 1);
        var first = StockPose(blend.Index, blend.Source, blend.Start, blend.Frames, firstFrame);
        if (amount <= 0) return CopyPose(first, sampledPose);
        CopyPose(StockPose(blend.Index, blend.Source, blend.Start, blend.Frames, firstFrame + 1), sampledPose);
        Blend(first, sampledPose, amount);
        return sampledPose;
    }

    private Dictionary<SceneObject, Matrix4x4> StockPose(int segment, int source, float start, int frames, int frame)
    {
        if (cachedSegment != segment || frame < cachedFrame - 1)
        {
            var precedingPose = PoseAt(start, source);
            if (!ReferenceEquals(precedingPose, stockPoseA)) CopyPose(precedingPose, stockPoseA);
            cachedSegment = segment;
            cachedFrame = -1;
            stockPoseB.Clear();
        }

        while (cachedFrame < frame)
        {
            int nextFrame = cachedFrame + 1;
            CopyPose(baseline, stockPoseB);
            ApplyLeadPose(stockPoseB, start + nextFrame / 60f, segment);
            Blend(stockPoseA, stockPoseB, (nextFrame + 1f) / (frames + 1));
            SwapStockPoseBuffers();
            cachedFrame = nextFrame;
        }

        return frame == cachedFrame ? stockPoseA : stockPoseB;
    }

    private void SwapStockPoseBuffers()
    {
        (stockPoseA, stockPoseB) = (stockPoseB, stockPoseA);
    }

    private void ApplyLeadPose(Dictionary<SceneObject, Matrix4x4> result, float time, int maxSegment)
    {
        if (!timeline!.TrySample(time, maxSegment, out var clip, out float trackTime,
            out Matrix4x4? rootInverse)) return;
        foreach (var track in clip.Tracks)
        {
            if (track.Bone < 0 || track.Bone >= actor.Bones.Count || actor.Bones[track.Bone] is not { } target) continue;
            int keyIndex = AnimationKeySearch.LastAtOrBefore(track.Keys, trackTime);
            if (keyIndex < 0) continue;
            AnimationKey key = track.Keys[keyIndex];
            if (key.Position == null && key.Rotation == null && key.Scale == null) continue;
            Matrix4x4.Decompose(result[target], out var scale, out var rotation, out var position);
            var sampled = CinematicTransformSampler.Evaluate(position, rotation, scale,
                track, trackTime, holdInactive: false);
            Matrix4x4.Decompose(sampled, out scale, out rotation, out position);
            actor.CompensateBodyRoot(target, key, rootInverse, ref position, ref rotation);
            result[target] = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation)
                * Matrix4x4.CreateTranslation(position);
        }
    }

    private void Blend(Dictionary<SceneObject, Matrix4x4> previous,
        Dictionary<SceneObject, Matrix4x4> current, float amount)
    {
        foreach (var target in uniqueBones)
        {
            if (target == actor.Root || !previous.TryGetValue(target, out var oldPose)
                || !current.TryGetValue(target, out var newPose)) continue;
            if (!Matrix4x4.Decompose(oldPose, out var oldScale, out var oldRotation, out var oldPosition)
                || !Matrix4x4.Decompose(newPose, out var newScale, out var newRotation, out var newPosition)) continue;
            current[target] = Matrix4x4.CreateScale(Vector3.Lerp(oldScale, newScale, amount))
                * Matrix4x4.CreateFromQuaternion(Quaternion.Slerp(oldRotation, newRotation, amount))
                * Matrix4x4.CreateTranslation(Vector3.Lerp(oldPosition, newPosition, amount));
        }
    }

    private static Dictionary<SceneObject, Matrix4x4> CopyPose(
        IReadOnlyDictionary<SceneObject, Matrix4x4> source,
        Dictionary<SceneObject, Matrix4x4> destination)
    {
        destination.Clear();
        foreach (var (member, pose) in source) destination[member] = pose;
        return destination;
    }
}
