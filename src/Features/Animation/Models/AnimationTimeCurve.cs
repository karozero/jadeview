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
            bool validTime = !float.IsNaN(time) && !float.IsNegativeInfinity(time);
            if (!validTime || !float.IsFinite(factor)
                || index > 0 && time < points[index - 1].Time)
            {
                float previousTime = index > 0 ? points[index - 1].Time : 0;
                throw new InvalidDataException($"Invalid animation time-curve point {index}: "
                    + $"time={time:R}, factor={factor:R}, previous time={previousTime:R}.");
            }
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
        int index = 0;
        while (index < Points.Count - 1 && Points[index + 1].Time <= elapsed)
        {
            var point = Points[index];
            var nextPoint = Points[index + 1];
            warped += (nextPoint.Time - point.Time) * point.Factor;
            index++;
        }
        var activePoint = Points[index];
        warped += (elapsed - activePoint.Time) * activePoint.Factor;
        return warped;
    }

    private static float ReadFloat(ReadOnlySpan<byte> bytes, int offset) =>
        BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]));
}

public sealed record AnimationTimePoint(float Time, float Factor);
