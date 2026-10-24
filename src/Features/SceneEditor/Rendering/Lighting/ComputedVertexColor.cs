using System.Numerics;

namespace JadeView;

/// <summary>
/// Inputs for the source-derived vertex paint/RLI stage. AmbientColor is the channel
/// selected by the caller from the world state (including DontUseAmbient2 selection).
/// </summary>
public readonly record struct ComputedVertexColorInput(
    uint RawPackedRli,
    uint EffectiveDrawFlags,
    Vector4 RliDestinationColor,
    float RliFactor,
    Vector4 AmbientColor);

/// <summary>
/// Keeps authored RLI separate from the per-draw computed color, matching Jade's
/// GEO_Render_InitRLI and LIGHT_ComputeVertexColor stages.
/// </summary>
public readonly record struct ComputedVertexColorResult(
    uint RawPackedRli,
    uint ComputedPackedColor,
    bool UsedRli,
    bool AppliedAmbient);
