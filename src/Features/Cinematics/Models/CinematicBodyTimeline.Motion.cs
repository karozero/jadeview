using System.Numerics;

namespace JadeView;

public sealed partial class CinematicBodyTimeline
{
    private AnimationTrack[] rootTracks = [];
    private KeyValuePair<float, Vector3>[] rootPositionSetEvents = [];
    private TransformData rootBaseline = new();
    private float[] motionTimes = [];
    private Matrix4x4[]? motionPoses;

    private void InitializeMotion(AnimationClip source, SceneActor actor,
        IEnumerable<(float Time, CinematicLiteralCall Call)>? additionalCalls)
    {
        rootBaseline = actor.Root.Transform.Clone();
        AnimationTrack[] scriptTransformTracks = CinematicScriptTransformTracks.Read(source, additionalCalls)
            .Where(track => track.Target == actor.Root.GaoKey).ToArray();
        rootPositionSetEvents = scriptTransformTracks
            .SelectMany(track => track.Keys)
            .Where(key => key.Position.HasValue)
            .OrderBy(key => key.Time)
            .Select(key => new KeyValuePair<float, Vector3>(key.Time, key.Position!.Value))
            .ToArray();
        AnimationTrack[] persistentScriptTracks = scriptTransformTracks
            .Where(track => track.Keys.Any(key => key.Scale.HasValue)).ToArray();
        rootTracks = source.Tracks.Concat(persistentScriptTracks)
            .Where(track => track.Bone < 0 && track.Target == actor.Root.GaoKey).ToArray();
        var times = new SortedSet<float> { 0, source.Duration };
        foreach (float boundary in PoseBoundaries()) times.Add(boundary);
        foreach (var key in rootTracks.SelectMany(track => track.Keys)) times.Add(key.Time);
        foreach (var (time, _) in rootPositionSetEvents) times.Add(time);
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
            motionPoses[0] = ApplyPositionSetAt(0,
                CinematicTransformSampler.Evaluate(rootBaseline, rootTracks, 0));
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
        pose = CinematicTransformSampler.Evaluate(position, rotation, scale,
            rootTracks, time, holdInactive: false);
        return ApplyPositionSetAt(time, pose);
    }

    private Matrix4x4 ApplyPositionSetAt(float time, Matrix4x4 pose)
    {
        // Literal OBJ_PosSet calls are one-shot setters. Reapplying them as sampled
        // tracks would erase ANI root displacement on every frame after the call.
        int low = 0;
        int high = rootPositionSetEvents.Length - 1;
        int matchingIndex = -1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            int comparison = rootPositionSetEvents[middle].Key.CompareTo(time);
            if (comparison < 0) low = middle + 1;
            else if (comparison > 0) high = middle - 1;
            else
            {
                matchingIndex = middle;
                break;
            }
        }
        if (matchingIndex < 0) return pose;

        pose.Translation = rootPositionSetEvents[matchingIndex].Value;
        return pose;
    }
}
