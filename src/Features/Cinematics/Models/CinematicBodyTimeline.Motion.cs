using System.Numerics;

namespace JadeView;

public sealed partial class CinematicBodyTimeline
{
    private AnimationTrack[] rootTracks = [];
    private TransformData rootBaseline = new();
    private float[] motionTimes = [];
    private Matrix4x4[]? motionPoses;

    private void InitializeMotion(AnimationClip source, SceneActor actor)
    {
        rootBaseline = actor.Root.Transform.Clone();
        rootTracks = source.Tracks.Concat(CinematicScriptTransformTracks.Read(source))
            .Where(track => track.Bone < 0 && track.Target == actor.Root.GaoKey).ToArray();
        var times = new SortedSet<float> { 0, source.Duration };
        foreach (float boundary in PoseBoundaries()) times.Add(boundary);
        foreach (var key in rootTracks.SelectMany(track => track.Keys)) times.Add(key.Time);
        foreach (var segment in segments)
        {
            times.Add(segment.Start);
            if (float.IsFinite(segment.End)) times.Add(segment.End);
        }
        for (int frame = 1; frame / 60f < source.Duration; frame++) times.Add(frame / 60f);
        motionTimes = times.Where(time => time >= 0 && time <= source.Duration).ToArray();
    }

    internal Matrix4x4 RootPose(float time)
    {
        if (motionPoses == null)
        {
            motionPoses = new Matrix4x4[motionTimes.Length];
            motionPoses[0] = CinematicTransformSampler.Evaluate(rootBaseline, rootTracks, 0);
            for (int index = 1; index < motionTimes.Length; index++)
                motionPoses[index] = AdvanceRoot(motionPoses[index - 1], motionTimes[index - 1], motionTimes[index]);
        }
        int position = Array.BinarySearch(motionTimes, time);
        if (position >= 0) return motionPoses[position];
        int previous = Math.Clamp(~position - 1, 0, motionTimes.Length - 1);
        return AdvanceRoot(motionPoses[previous], motionTimes[previous], time);
    }

    private Matrix4x4 AdvanceRoot(Matrix4x4 pose, float previousTime, float time)
    {
        var previousPose = pose;
        var segment = segments.LastOrDefault(segment => segment.Start <= previousTime);
        if (segment != null)
        {
            var previous = segment.MotionAt(previousTime);
            if (Matrix4x4.Invert(previous, out var inverse))
                pose = segment.MotionAt(time) * inverse * pose;
            pose = BlendRootTranslation(previousPose, pose, segment, previousTime, time);
        }
        Matrix4x4.Decompose(pose, out var scale, out var rotation, out var position);
        return CinematicTransformSampler.Evaluate(position, rotation, scale,
            rootTracks, time, holdInactive: false);
    }
}
