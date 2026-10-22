using System.Numerics;

namespace JadeView;

internal sealed record CinematicFlashController(
    uint OwnerKey,
    uint SnapActorKey,
    Vector3 Offset,
    float AppearDelay,
    float DisappearDelay,
    float ExtrudeDistance,
    float LengthRatio,
    uint Color,
    int RayCount,
    float BigAlpha,
    float BigLengthMin,
    float BigLengthMax,
    float BigWidthMin,
    float BigWidthMax,
    float SmallAlpha,
    float SmallLengthMin,
    float SmallLengthMax,
    float SmallWidthMin,
    float SmallWidthMax,
    ushort InitialCapabilities);

internal readonly record struct CinematicFlashState(Vector3 Position, uint Seed, float CameraPullDistance,
    float LengthRatio, uint Color, int RayCount,
    float Opacity, float BigAngleJitter, float BigLengthMin, float BigLengthMax,
    float BigWidthMin, float BigWidthMax, float SmallAngleJitter,
    float SmallLengthMin, float SmallLengthMax, float SmallWidthMin, float SmallWidthMax);
