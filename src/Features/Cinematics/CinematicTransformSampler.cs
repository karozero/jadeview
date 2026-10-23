using System.Numerics;

namespace JadeView;

internal static class CinematicTransformSampler
{
    internal static Matrix4x4 Evaluate(TransformData baseline, IEnumerable<AnimationTrack> tracks, float time,
        bool holdInactive = true)
        => Evaluate(baseline.Translation, baseline.Rotation, baseline.Scale, tracks, time, holdInactive);

    internal static Matrix4x4 Evaluate(Vector3 position, Quaternion rotation, Vector3 scale,
        IEnumerable<AnimationTrack> tracks, float time, bool holdInactive = true)
    {
        foreach (var track in tracks) Apply(track, time, holdInactive, ref position, ref rotation, ref scale);
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
    }

    internal static Matrix4x4 Evaluate(TransformData baseline, AnimationTrack track, float time,
        bool holdInactive = true)
        => Evaluate(baseline.Translation, baseline.Rotation, baseline.Scale, track, time, holdInactive);

    internal static Matrix4x4 Evaluate(Vector3 position, Quaternion rotation, Vector3 scale,
        AnimationTrack track, float time, bool holdInactive = true)
    {
        Apply(track, time, holdInactive, ref position, ref rotation, ref scale);
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
    }

    internal static Matrix4x4 EvaluateRoot(AnimationClip clip, float time)
    {
        Vector3 position = Vector3.Zero;
        Quaternion rotation = Quaternion.Identity;
        Vector3 scale = Vector3.One;
        foreach (var track in clip.Tracks)
        {
            if (track.Bone < 0 && track.Target == 0)
                Apply(track, time, true, ref position, ref rotation, ref scale);
        }
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation)
            * Matrix4x4.CreateTranslation(position);
    }

    private static void Apply(AnimationTrack track, float time, bool holdInactive,
        ref Vector3 position, ref Quaternion rotation, ref Vector3 scale)
    {
        if (track.CinematicPlaybackClock is { } playbackClock)
        {
            if (!playbackClock.TryMap(time, out time)) return;
        }

        if (FindKeys(track, time, holdInactive, TrackComponent.Position) is { } translation)
        {
            var first = translation.First;
            var start = first.Position!.Value;
            position = AnimationInterpolation.Position(first, start, translation.Next.Position ?? start,
                AnimationInterpolation.Amount(first, time));
        }
        if (FindKeys(track, time, holdInactive, TrackComponent.Rotation) is { } orientation)
        {
            var start = orientation.First.Rotation!.Value;
            rotation = Quaternion.Slerp(start, orientation.Next.Rotation ?? start,
                AnimationInterpolation.Amount(orientation.First, time));
        }
        if (FindKeys(track, time, holdInactive, TrackComponent.Scale) is { } scaling)
        {
            var start = scaling.First.Scale!.Value;
            scale = Vector3.Lerp(start, scaling.Next.Scale ?? start,
                AnimationInterpolation.Amount(scaling.First, time));
        }
    }

    private static (AnimationKey First, AnimationKey Next)? FindKeys(AnimationTrack track, float time,
        bool holdInactive, TrackComponent component)
    {
        int index = AnimationKeySearch.LastAtOrBefore(track.Keys, time);
        while (index >= 0)
        {
            var first = track.Keys[index];
            bool hasComponent = component switch
            {
                TrackComponent.Position => first.Position != null,
                TrackComponent.Rotation => first.Rotation != null,
                TrackComponent.Scale => first.Scale != null,
                _ => false
            };
            if (hasComponent)
                return (first, index + 1 < track.Keys.Count ? track.Keys[index + 1] : first);
            if (!holdInactive) break;
            index--;
        }
        return null;
    }

    private enum TrackComponent { Position, Rotation, Scale }
}
