using System.Numerics;

namespace JadeView;

internal sealed class CinematicBodyPoseSampler(SceneActor actor)
{
    private CinematicBodyTimeline? timeline;
    private Dictionary<SceneObject, Matrix4x4> baseline = [];
    private readonly Dictionary<(int Segment, int Frame), Dictionary<SceneObject, Matrix4x4>> stock = [];

    internal Dictionary<SceneObject, Matrix4x4> Evaluate(CinematicBodyTimeline body, float time,
        Dictionary<SceneObject, Matrix4x4> initial)
    {
        bool changed = timeline != body || baseline.Count != initial.Count
            || initial.Any(pair => !baseline.TryGetValue(pair.Key, out var previous) || previous != pair.Value);
        if (changed)
        {
            timeline = body;
            baseline = new(initial);
            stock.Clear();
        }
        return PoseAt(time, int.MaxValue);
    }

    private Dictionary<SceneObject, Matrix4x4> PoseAt(float time, int maxSegment)
    {
        if (timeline!.BlendAt(time, maxSegment) is not { } blend) return LeadPose(time, maxSegment);
        float frames = (blend.Time - blend.Start) * 60;
        int firstFrame = (int)MathF.Floor(frames + .0001f);
        float amount = Math.Clamp(frames - firstFrame, 0, 1);
        var first = StockPose(blend.Index, blend.Source, blend.Start, blend.Frames, firstFrame);
        if (amount <= 0) return new(first);
        var pose = new Dictionary<SceneObject, Matrix4x4>(
            StockPose(blend.Index, blend.Source, blend.Start, blend.Frames, firstFrame + 1));
        Blend(first, pose, amount);
        return pose;
    }

    private Dictionary<SceneObject, Matrix4x4> StockPose(int segment, int source, float start, int frames, int frame)
    {
        if (frame < 0) return PoseAt(start, source);
        if (stock.TryGetValue((segment, frame), out var cached)) return cached;
        var previous = StockPose(segment, source, start, frames, frame - 1);
        var current = LeadPose(start + frame / 60f, segment);
        Blend(previous, current, (frame + 1f) / (frames + 1));
        stock[(segment, frame)] = current;
        return current;
    }

    private Dictionary<SceneObject, Matrix4x4> LeadPose(float time, int maxSegment)
    {
        var result = new Dictionary<SceneObject, Matrix4x4>(baseline);
        foreach (var (track, trackTime, rootInverse) in timeline!.Sample(time, maxSegment))
        {
            if (track.Bone < 0 || track.Bone >= actor.Bones.Count || actor.Bones[track.Bone] is not { } target) continue;
            var key = track.Keys.LastOrDefault(key => key.Time <= trackTime);
            if (key == null || key.Position == null && key.Rotation == null && key.Scale == null) continue;
            Matrix4x4.Decompose(result[target], out var scale, out var rotation, out var position);
            var sampled = CinematicTransformSampler.Evaluate(new TransformData
            { Scale = scale, Rotation = rotation, Translation = position }, [track], trackTime, holdInactive: false);
            Matrix4x4.Decompose(sampled, out scale, out rotation, out position);
            actor.CompensateBodyRoot(target, key, rootInverse, ref position, ref rotation);
            result[target] = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation)
                * Matrix4x4.CreateTranslation(position);
        }
        return result;
    }

    private void Blend(Dictionary<SceneObject, Matrix4x4> previous,
        Dictionary<SceneObject, Matrix4x4> current, float amount)
    {
        foreach (var target in actor.Bones.OfType<SceneObject>().Distinct())
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
}
