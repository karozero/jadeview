using System.Numerics;

namespace JadeView;

public sealed partial class CinematicBodyTimeline
{
    private sealed record Segment(float Start, int ActionId, CinematicAction Action, int? Frequency, float StartFrame)
    {
        internal float End = float.PositiveInfinity;
        internal int? PendingActionId;
        internal byte BlendFrames, BlendFlags;
        internal int BlendSource = -1;
        internal readonly List<CinematicFrequencyChange> FrequencyChanges = [];

        internal (AnimationClip Clip, float Time, int ItemIndex)? SampleAt(float time)
        {
            var clock = ClockAt(time);
            return Action.Sample(clock.Elapsed, clock.Frequency, StartFrame);
        }

        internal Matrix4x4 MotionAt(float time)
        {
            var clock = ClockAt(time);
            return Action.Motion(clock.Elapsed, clock.Frequency, StartFrame);
        }

        internal bool HasFinishedAt(float time)
        {
            var clock = ClockAt(time);
            return Action.HasFinished(clock.Elapsed, clock.Frequency, StartFrame);
        }

        private (float Elapsed, int? Frequency) ClockAt(float time)
        {
            float elapsed = Math.Min(time, End) - Start;
            return FrequencyChanges.Count == 0 ? (elapsed, Frequency)
                : (CinematicActionClock.SourceElapsed(Action, elapsed, Frequency, StartFrame, FrequencyChanges), 64);
        }
    }
}
