using System.Numerics;

namespace JadeView;

internal static class AnimationInterpolation
{
    internal const int NoInterpolation = 0x0800;

    internal static float Amount(AnimationKey key, float time)
    {
        if (key.Duration <= 0 || (key.Flags & NoInterpolation) != 0) return 0;
        float elapsed = Math.Clamp(time - key.Time, 0, key.Duration);
        float warped = key.TimeCurve?.Evaluate(elapsed) ?? elapsed;
        if (!float.IsFinite(warped)) throw new InvalidDataException("Animation time curve overflowed.");
        return Math.Min(warped / key.Duration, 1);
    }

    internal static Vector3 Position(AnimationKey first, Vector3 start, Vector3 end, float amount)
    {
        float inverse = 1 - amount;
        return (first.Format & 3) switch
        {
            2 => start * inverse * inverse + 2 * (first.Control1 ?? start) * inverse * amount + end * amount * amount,
            3 => start * inverse * inverse * inverse + 3 * (first.Control1 ?? start) * inverse * inverse * amount
                + 3 * (first.Control2 ?? end) * inverse * amount * amount + end * amount * amount * amount,
            _ => Vector3.Lerp(start, end, amount)
        };
    }
}
