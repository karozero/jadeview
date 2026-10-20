using System.Numerics;

namespace JadeView;

internal sealed record CinematicFlashController(
    uint OwnerKey,
    uint SnapActorKey,
    Vector3 Offset,
    float AppearDelay,
    float DisappearDelay,
    float ExtrudeDistance,
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

internal readonly record struct CinematicFlashState(Vector3 Position, uint Color, int RayCount,
    float Opacity, float BigAlpha, float BigLength, float BigWidth,
    float SmallAlpha, float SmallLength, float SmallWidth);
