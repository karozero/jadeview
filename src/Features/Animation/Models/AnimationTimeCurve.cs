using System.Buffers.Binary;

namespace JadeView;

public sealed class AnimationTimeCurve
{
    public IReadOnlyList<AnimationTimePoint> Points { get; }

    private AnimationTimeCurve(AnimationTimePoint[] points) => Points = Array.AsReadOnly(points);

    internal static AnimationTimeCurve Read(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8) throw new InvalidDataException("Truncated animation time curve.");
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        if (count == 0 || count > 100000 || payload.Length != (long)count * 8)
            throw new InvalidDataException("Invalid animation time-curve point count.");
        var points = new AnimationTimePoint[(int)count];
        for (int index = 0; index < points.Length; index++)
        {
            float time = index == 0 ? 0 : ReadFloat(payload, index * 8);
            float factor = ReadFloat(payload, index * 8 + 4);
            if (!float.IsFinite(time) || !float.IsFinite(factor)
                || index > 0 && time <= points[index - 1].Time)
                throw new InvalidDataException("Animation time-curve points must be finite and ordered.");
            points[index] = new(time, factor);
        }
        return new(points);
    }

    // EVE_f_Event_InterpolationKey_InterpolateTime integrates held factors;
    // the factors themselves are not interpolated between their timestamps.
    internal float Evaluate(float elapsed)
    {
        float warped = 0;
        elapsed = Math.Max(0, elapsed);
        for (int index = 0; index < Points.Count; index++)
        {
            var point = Points[index];
            float end = index + 1 < Points.Count ? Math.Min(elapsed, Points[index + 1].Time) : elapsed;
            warped += (end - point.Time) * point.Factor;
            if (end >= elapsed) break;
        }
        return warped;
    }

    private static float ReadFloat(ReadOnlySpan<byte> bytes, int offset) =>
        BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]));
}

public sealed record AnimationTimePoint(float Time, float Factor);
