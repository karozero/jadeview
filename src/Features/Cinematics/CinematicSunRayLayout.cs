using System.Numerics;

namespace JadeView;

internal static class CinematicSunRayLayout
{
    internal static CinematicSunRay[] Create(Vector2 center, int requestedRayCount, uint seed,
        float bigAngleJitter, float bigLengthMin, float bigLengthMax,
        float bigWidthMin, float bigWidthMax, float smallAngleJitter,
        float smallLengthMin, float smallLengthMax, float smallWidthMin, float smallWidthMax,
        float lengthRatio, float horizontalScale, float verticalScale, float maximumRadius)
    {
        int rayCount = Math.Clamp(requestedRayCount & ~1, 0, 128);
        if (rayCount == 0) return [];

        float largestExtent = Math.Max(
            RayExtent(Math.Max(bigLengthMin, bigLengthMax), Math.Max(bigWidthMin, bigWidthMax), lengthRatio, horizontalScale, verticalScale),
            RayExtent(Math.Max(smallLengthMin, smallLengthMax), Math.Max(smallWidthMin, smallWidthMax), lengthRatio, horizontalScale, verticalScale));
        float previewScale = largestExtent > maximumRadius && maximumRadius > 0
            ? maximumRadius / largestExtent : 1;
        bigLengthMin *= previewScale;
        bigLengthMax *= previewScale;
        bigWidthMin *= previewScale;
        bigWidthMax *= previewScale;
        smallLengthMin *= previewScale;
        smallLengthMax *= previewScale;
        smallWidthMin *= previewScale;
        smallWidthMax *= previewScale;

        int pairCount = rayCount / 2;
        var rays = new CinematicSunRay[rayCount];
        for (int pairIndex = 0; pairIndex < pairCount; pairIndex++)
        {
            float baseAngle = pairIndex * (2 * MathF.PI / pairCount);
            rays[pairIndex * 2] = CreateRay(center, baseAngle, bigAngleJitter,
                bigLengthMin, bigLengthMax, bigWidthMin, bigWidthMax,
                lengthRatio, horizontalScale, verticalScale, ref seed, false);
            rays[pairIndex * 2 + 1] = CreateRay(center, baseAngle, smallAngleJitter,
                smallLengthMin, smallLengthMax, smallWidthMin, smallWidthMax,
                lengthRatio, horizontalScale, verticalScale, ref seed, true);
        }

        return rays;
    }

    private static float RayExtent(float length, float width, float lengthRatio,
        float horizontalScale, float verticalScale)
    {
        float rearExtent = MathF.Abs(length * horizontalScale);
        float forwardExtent = MathF.Sqrt(MathF.Pow(length * lengthRatio * horizontalScale, 2)
            + MathF.Pow(width * verticalScale, 2));
        return Math.Max(rearExtent, forwardExtent);
    }

    private static CinematicSunRay CreateRay(Vector2 center, float baseAngle,
        float angleJitter, float lengthMin, float lengthMax, float widthMin, float widthMax,
        float lengthRatio, float horizontalScale, float verticalScale, ref uint seed, bool isSmall)
    {
        float angle = baseAngle + NextSigned(ref seed) * angleJitter;
        float length = NextRange(ref seed, lengthMin, lengthMax);
        float width = NextRange(ref seed, widthMin, widthMax);
        var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var side = new Vector2(-direction.Y, direction.X);
        var rear = center - direction * length * horizontalScale;
        var forward = center + direction * length * lengthRatio * horizontalScale;
        var halfWidth = side * width * verticalScale;

        return new(rear, forward + halfWidth, forward - halfWidth, isSmall);
    }

    private static float NextSigned(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state / (float)uint.MaxValue) * 2 - 1;
    }

    private static float NextRange(ref uint state, float minimum, float maximum) =>
        minimum + (maximum - minimum) * ((NextSigned(ref state) + 1) * .5f);
}

internal readonly record struct CinematicSunRay(Vector2 Rear, Vector2 ForwardLeft,
    Vector2 ForwardRight, bool IsSmall);
