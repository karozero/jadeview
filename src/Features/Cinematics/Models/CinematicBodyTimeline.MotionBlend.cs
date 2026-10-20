using System.Numerics;

namespace JadeView;

public sealed partial class CinematicBodyTimeline
{
    private const byte FreezeRootBlendFlag = 0x08;

    private Matrix4x4 BlendRootTranslation(Matrix4x4 previousPose, Matrix4x4 leadPose,
        Segment segment, float previousTime, float time)
    {
        bool active = segment.BlendFrames > 0 && segment.BlendSource >= 0
            && previousTime < segment.Start + segment.BlendFrames / 60f;
        if (!active || (segment.BlendFlags & FreezeRootBlendFlag) != 0) return leadPose;
        var outgoing = segments[segment.BlendSource];
        if (UsesDynamics(segment, previousTime) || UsesDynamics(outgoing, previousTime)) return leadPose;

        int anchorIndex = Array.BinarySearch(motionTimes, segment.Start);
        if (anchorIndex < 0 || motionPoses == null) return leadPose;
        var anchor = motionPoses[anchorIndex];
        var initial = MotionAt(outgoing, segment.Start);
        var oldStart = MotionAt(outgoing, previousTime);
        if (!Matrix4x4.Invert(initial, out var inverseInitial)
            || !Matrix4x4.Invert(oldStart, out var inverseStart)) return leadPose;

        // ANI_b_BlendMagicBox retains the lead rotation and adjusts both Flash
        // translations after every step. Integrating displacements preserves that
        // corrected position instead of snapping back to either original path.
        var oldPose = oldStart * inverseInitial * anchor;
        var oldEnd = MotionAt(outgoing, time) * inverseStart * oldPose;
        var oldPosition = previousPose.Translation + oldEnd.Translation - oldPose.Translation;
        float elapsed = Math.Min(time, segment.End) - segment.Start;
        float amount = Math.Clamp((elapsed * 60 + 1) / (segment.BlendFrames + 1), 0, 1);
        leadPose.Translation = Vector3.Lerp(oldPosition, leadPose.Translation, amount);
        return leadPose;
    }

    private static Matrix4x4 MotionAt(Segment segment, float time) =>
        segment.MotionAt(time);

    private static bool UsesDynamics(Segment segment, float time)
    {
        var sample = segment.SampleAt(time);
        return sample is { } current
            && segment.Action.Items[current.ItemIndex].UsesDynamicsTranslation;
    }
}
