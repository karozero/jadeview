namespace JadeView;

public sealed partial class CinematicBodyTimeline
{
    internal IEnumerable<float> PoseBoundaries()
    {
        foreach (var segment in segments)
        {
            yield return segment.Start;
            foreach (var change in segment.FrequencyChanges) yield return segment.Start + change.Time;
            if (float.IsFinite(segment.End)) yield return segment.End;
            for (int frame = 1; frame <= segment.BlendFrames; frame++)
                yield return segment.Start + frame / 60f;
        }
    }

    internal (int Index, int Source, float Start, int Frames, float Time)? BlendAt(float time, int maxSegment)
    {
        for (int index = Math.Min(maxSegment, segments.Count - 1); index >= 0; index--)
        {
            var segment = segments[index];
            if (segment.Start > time) continue;
            float elapsed = Math.Min(time, segment.End) - segment.Start;
            if (segment.BlendFrames == 0 || segment.BlendSource < 0 || elapsed >= segment.BlendFrames / 60f)
                return null;
            return (index, segment.BlendSource, segment.Start, segment.BlendFrames, Math.Min(time, segment.End));
        }
        return null;
    }

    private Segment WithBlend(Segment segment, CinematicActionTransition? transition)
    {
        if (transition == null || segments.Count == 0) return segment;
        segment.BlendFrames = transition.BlendFrames;
        segment.BlendFlags = transition.Flags;
        segment.BlendSource = segments.Count - 1;
        return segment;
    }
}
