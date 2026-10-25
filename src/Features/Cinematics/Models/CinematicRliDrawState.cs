using System.Numerics;

namespace JadeView;

/// <summary>
/// RLI inputs temporarily installed by a source-confirmed per-object draw callback.
/// The override is scoped to one GAO draw; callers must not mutate the scene-wide
/// cinematic lighting state while resolving it.
/// </summary>
internal readonly record struct CinematicRliDrawState(
    Vector4 DestinationColor, float Factor, bool FactorIsUsed,
    string? SourceFunction = null)
{
    private const uint DontScaleRli = 0x02000000;

    internal static CinematicRliDrawState Resolve(Vector4 sceneDestinationColor,
        float sceneFactor, CinematicRliDrawState? objectOverride) =>
        objectOverride ?? new(sceneDestinationColor, sceneFactor, FactorIsUsed: false);

    internal static uint ApplyRliFactorUsage(uint drawFlags, bool factorIsUsed) =>
        factorIsUsed ? drawFlags & ~DontScaleRli : drawFlags | DontScaleRli;
}
