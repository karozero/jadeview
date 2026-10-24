using System.Numerics;

namespace JadeView;

/// <summary>
/// Applies decoded EVE OBJMove displacement after the base scene-object pose is sampled.
/// Actor roots and hierarchy-bound targets remain unsupported until their shared pose
/// composition and parent-space behavior are verified.
/// </summary>
internal sealed partial class CinematicGameScriptMoveTimeline
{
    private readonly Dictionary<SceneObject, GameScriptMoveSegment[]> segmentsByTarget;
    private readonly Dictionary<SceneObject, float[]> resetTimesByTarget;
    private readonly SceneObject[] targets;
    private readonly string[] diagnostics;

    internal IReadOnlyCollection<SceneObject> Targets => targets;
    internal IReadOnlyList<string> Diagnostics => diagnostics;
    internal IReadOnlyList<float> ChangeTimes => segmentsByTarget.Values
        .SelectMany(targetSegments => targetSegments)
        .SelectMany(segment => new[] { segment.TimelineStartSeconds, segment.TimelineEndSeconds })
        .Concat(resetTimesByTarget.Values.SelectMany(times => times))
        .Where(float.IsFinite).Distinct().Order().ToArray();

    internal Matrix4x4 Apply(SceneObject target, Matrix4x4 basePose, float time)
    {
        if (!segmentsByTarget.TryGetValue(target, out var segments) || !float.IsFinite(time))
            return basePose;

        var resets = resetTimesByTarget[target];
        float resetTime = resets.LastOrDefault(candidate => candidate <= time, float.NegativeInfinity);
        Vector3 displacement = Vector3.Zero;
        foreach (var segment in segments)
        {
            GameScriptMoveResolution atTime = segment.EvaluateAt(time);
            if (!atTime.IsResolved) continue;
            Vector3 segmentDisplacement = atTime.Displacement;
            if (float.IsFinite(resetTime))
            {
                GameScriptMoveResolution atReset = segment.EvaluateAt(resetTime);
                if (atReset.IsResolved) segmentDisplacement -= atReset.Displacement;
            }
            displacement += segmentDisplacement;
        }

        basePose.M41 += displacement.X;
        basePose.M42 += displacement.Y;
        basePose.M43 += displacement.Z;
        return basePose;
    }

    internal void ResetTargets()
    {
        foreach (var target in targets) target.PreviewMatrix = null;
    }

    internal void Evaluate(float time)
    {
        foreach (var target in targets)
        {
            Matrix4x4 basePose = target.PreviewMatrix ?? AnimationMath.Trs(target.Transform);
            target.PreviewMatrix = Apply(target, basePose, time);
        }
    }

}
