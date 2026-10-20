using System.Numerics;

namespace JadeView;

internal static class CinematicTransformSampler
{
    internal static Matrix4x4 Evaluate(TransformData baseline, IEnumerable<AnimationTrack> tracks, float time,
        bool holdInactive = true)
    {
        Vector3 position = baseline.Translation;
        Quaternion rotation = baseline.Rotation;
        Vector3 scale = baseline.Scale;
        foreach (var track in tracks) Apply(track, time, holdInactive, ref position, ref rotation, ref scale);
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
    }

    private static void Apply(AnimationTrack track, float time, bool holdInactive,
        ref Vector3 position, ref Quaternion rotation, ref Vector3 scale)
    {
        if (FindKeys(track, time, holdInactive, key => key.Position != null) is { } translation)
        {
            var first = translation.First;
            var start = first.Position!.Value;
            position = AnimationInterpolation.Position(first, start, translation.Next.Position ?? start,
                AnimationInterpolation.Amount(first, time));
        }
        if (FindKeys(track, time, holdInactive, key => key.Rotation != null) is { } orientation)
        {
            var start = orientation.First.Rotation!.Value;
            rotation = Quaternion.Slerp(start, orientation.Next.Rotation ?? start,
                AnimationInterpolation.Amount(orientation.First, time));
        }
        if (FindKeys(track, time, holdInactive, key => key.Scale != null) is { } scaling)
        {
            var start = scaling.First.Scale!.Value;
            scale = Vector3.Lerp(start, scaling.Next.Scale ?? start,
                AnimationInterpolation.Amount(scaling.First, time));
        }
    }

    private static (AnimationKey First, AnimationKey Next)? FindKeys(AnimationTrack track, float time,
        bool holdInactive, Func<AnimationKey, bool> hasComponent)
    {
        int index = track.Keys.FindLastIndex(key => key.Time <= time);
        while (index >= 0)
        {
            var first = track.Keys[index];
            if (hasComponent(first))
                return (first, index + 1 < track.Keys.Count ? track.Keys[index + 1] : first);
            if (!holdInactive) break;
            index--;
        }
        return null;
    }
}
