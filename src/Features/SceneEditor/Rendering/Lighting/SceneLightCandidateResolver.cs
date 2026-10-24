using System.Numerics;

namespace JadeView;

internal enum SceneLightSectorMembership
{
    GlobalWorld,
    ActiveSector,
    OutsideActiveSector,
    Unknown
}

internal sealed record SceneLightCandidateScope(
    bool SectorizationEnabled,
    IReadOnlyDictionary<SceneObject, SceneLightSectorMembership>? MembershipByObject = null)
{
    // JadeView's scene preview renders the complete supplied WOL/assembled world without
    // native sector culling. This is a selected-world scope, not every loaded BF object.
    internal static SceneLightCandidateScope FullWorldPreview { get; } = new(false);
}

internal sealed record SceneLightCandidate(SceneObject Owner, SceneLight Light);

internal sealed record SceneLightReceiverBoundsContext(
    PcObjectBounds? Bounds,
    Vector3 WorldPosition,
    IReadOnlyDictionary<SceneObject, Matrix4x4> GameWorldMatrices,
    string? PositionDiagnostic = null,
    SceneObject? PositionReferenceOwner = null,
    Matrix4x4 PositionReferenceMatrix = default,
    bool BoundsAreRuntimeDynamic = false);

internal enum SceneLightCandidateDiagnosticCode
{
    ReceiverNotLighted,
    UnsupportedSectorScope,
    UnknownSectorMembership,
    OutsideActiveSector,
    MissingLightComponent,
    Inactive,
    ShadowBufferOwned,
    ReceiverMaskRejected,
    LightReferenceMismatch,
    ReceiverBoundsTestUnavailable,
    ReceiverDynamicBoundsUnavailable,
    UnsupportedReceiverBounds,
    ReceiverWorldPositionUnavailable,
    LightWorldPositionUnavailable,
    ReceiverOutsideLightRange,
    UnsupportedLightType
}

internal sealed record SceneLightCandidateDiagnostic(
    SceneObject? Owner,
    SceneLightCandidateDiagnosticCode Code,
    string Detail);

internal sealed record SceneLightCandidateResolution(
    IReadOnlyList<SceneLightCandidate> Diffuse,
    IReadOnlyList<SceneLightCandidate> Fog,
    IReadOnlyList<SceneLightCandidate> AdditionalMaterial,
    IReadOnlyList<SceneLightCandidate> Unsupported,
    IReadOnlyList<SceneLightCandidateDiagnostic> Diagnostics);

/// <summary>
/// Resolves the ordered light candidates for one receiver from one selected loaded world.
/// The source world object list and optional per-playhead light-state snapshot are explicit;
/// this does not scan other document roots or emulate world/callback loading.
/// </summary>
internal static class SceneLightCandidateResolver
{
    private const uint LightIdentityFlag = 0x00000010;
    private const uint LightedDrawMask = 0x08000000;
    private const uint CheckReceiverBoundsDrawMask = 0x00200000;
    private const uint Active = 0x00000008;
    private const uint DynamicReceiver = 0x00000100;
    private const uint StaticReceiver = 0x00000200;
    private const uint UseShadowBuffer = 0x40000000;
    private const uint DynamicObjectFlag = 0x00000008;

    internal static SceneLightCandidateResolution Resolve(
        WorldData selectedWorld,
        uint receiverFlags2,
        uint receiverDrawMask,
        uint receiverInitialActorKey,
        CinematicLightStateEvaluation? evaluatedLightState = null,
        SceneLightCandidateScope? scope = null,
        SceneLightReceiverBoundsContext? receiverBoundsContext = null)
    {
        ArgumentNullException.ThrowIfNull(selectedWorld);
        SceneLightCandidateScope resolvedScope = scope ?? SceneLightCandidateScope.FullWorldPreview;
        var diffuse = new List<SceneLightCandidate>();
        var fog = new List<SceneLightCandidate>();
        var additionalMaterial = new List<SceneLightCandidate>();
        var unsupported = new List<SceneLightCandidate>();
        var diagnostics = new List<SceneLightCandidateDiagnostic>();

        bool receiverRequestsLighting = (receiverDrawMask & LightedDrawMask) != 0;
        if (!receiverRequestsLighting)
            diagnostics.Add(new(null, SceneLightCandidateDiagnosticCode.ReceiverNotLighted,
                "The receiver draw mask does not request per-object dynamic lighting; world fog is still resolved separately."));

        if (resolvedScope.SectorizationEnabled && resolvedScope.MembershipByObject == null)
        {
            diagnostics.Add(new(null, SceneLightCandidateDiagnosticCode.UnsupportedSectorScope,
                "Sectorized lighting requires explicit per-object active-sector membership."));
            return CreateResolution();
        }

        Dictionary<SceneObject, SceneLight> evaluatedLights = evaluatedLightState?.Lights
            .ToDictionary(state => state.Receiver, state => state.Light)
            ?? new Dictionary<SceneObject, SceneLight>();
        uint requiredReceiverFlag = (receiverFlags2 & DynamicObjectFlag) != 0
            ? DynamicReceiver
            : StaticReceiver;

        // WorldData.Objects preserves the selected WOL/assembled-world order. The runtime
        // light list is likewise traversed in source list order; do not sort or key-deduplicate.
        foreach (SceneObject owner in selectedWorld.Objects)
        {
            if ((owner.Flags & LightIdentityFlag) == 0)
                continue;

            if (!IsInScope(owner, resolvedScope, out SceneLightCandidateDiagnosticCode? scopeError))
            {
                if (scopeError is SceneLightCandidateDiagnosticCode code)
                    diagnostics.Add(new(owner, code, SectorDetail(code)));
                continue;
            }

            SceneLight? light = evaluatedLights.GetValueOrDefault(owner) ?? owner.Light;
            if (owner.LightResourceKey is 0 or uint.MaxValue || light == null)
            {
                diagnostics.Add(new(owner, SceneLightCandidateDiagnosticCode.MissingLightComponent,
                    "The light identity GAO has no resolved non-null PC light resource component."));
                continue;
            }

            if ((light.Flags & Active) == 0)
            {
                diagnostics.Add(new(owner, SceneLightCandidateDiagnosticCode.Inactive,
                    "The effective light component is inactive at this preview time."));
                continue;
            }

            SceneLightCandidate candidate = new(owner, light);
            if (light.KnownType == SceneLightType.Fog)
            {
                // Both source world renderers configure fog while building the light list,
                // rather than sending it through per-vertex point-light accumulation.
                fog.Add(candidate);
                continue;
            }

            if (!receiverRequestsLighting)
                continue;

            if ((light.Flags & UseShadowBuffer) != 0)
            {
                diagnostics.Add(new(owner, SceneLightCandidateDiagnosticCode.ShadowBufferOwned,
                    "The desktop source path reserves this light for the shadow-buffer pass."));
                continue;
            }

            if ((light.Flags & requiredReceiverFlag) == 0)
            {
                diagnostics.Add(new(owner, SceneLightCandidateDiagnosticCode.ReceiverMaskRejected,
                    "The effective light receiver flags do not include this object class."));
                continue;
            }

            if (light.HasLightGaoReference && light.LightGaoKey != receiverInitialActorKey)
            {
                diagnostics.Add(new(owner, SceneLightCandidateDiagnosticCode.LightReferenceMismatch,
                    "The referenced light GAO does not match the receiver's initial actor reference."));
                continue;
            }

            if ((receiverDrawMask & CheckReceiverBoundsDrawMask) != 0
                && (light.KnownType is SceneLightType.Omni or SceneLightType.Spot))
            {
                if (receiverBoundsContext?.PositionDiagnostic is string positionError)
                {
                    diagnostics.Add(new(owner, SceneLightCandidateDiagnosticCode.ReceiverWorldPositionUnavailable,
                        positionError));
                    continue;
                }

                if (receiverBoundsContext?.Bounds is not PcObjectBounds bounds)
                {
                    diagnostics.Add(new(owner, SceneLightCandidateDiagnosticCode.ReceiverBoundsTestUnavailable,
                        "The source draw mask requests a light-radius/AABB test, but this receiver has no parsed PC bounds."));
                    continue;
                }

                if (receiverBoundsContext.BoundsAreRuntimeDynamic)
                {
                    diagnostics.Add(new(owner, SceneLightCandidateDiagnosticCode.ReceiverDynamicBoundsUnavailable,
                        "The receiver's animated bounds are recomputed from its current skeleton by the game; JadeView only has the serialized bind/default bounds."));
                    continue;
                }

                if (bounds.Kind is not (PcObjectBoundsKind.AxisAlignedBox or PcObjectBoundsKind.OrientedBox))
                {
                    diagnostics.Add(new(owner, SceneLightCandidateDiagnosticCode.UnsupportedReceiverBounds,
                        $"The receiver bounds kind {bounds.Kind} is preserved but not used by the verified light-range test."));
                    continue;
                }

                if (receiverBoundsContext.GameWorldMatrices == null
                    || !receiverBoundsContext.GameWorldMatrices.TryGetValue(owner, out Matrix4x4 lightMatrix))
                {
                    diagnostics.Add(new(owner, SceneLightCandidateDiagnosticCode.LightWorldPositionUnavailable,
                        "The bounded light has no current game-space world matrix."));
                    continue;
                }

                Vector3 lightRelativeToReceiver = lightMatrix.Translation - receiverBoundsContext.WorldPosition;
                if (!IntersectsSphereAabb(lightRelativeToReceiver, light.Far, bounds.Minimum, bounds.Maximum))
                {
                    diagnostics.Add(new(owner, SceneLightCandidateDiagnosticCode.ReceiverOutsideLightRange,
                        "The receiver's serialized outer AABB does not intersect this omni/spot light's far-radius test."));
                    continue;
                }
            }

            switch (light.KnownType)
            {
                case SceneLightType.Omni:
                case SceneLightType.Direct:
                case SceneLightType.Spot:
                    diffuse.Add(candidate);
                    break;
                case SceneLightType.AddMaterial:
                    additionalMaterial.Add(candidate);
                    break;
                default:
                    unsupported.Add(candidate);
                    diagnostics.Add(new(owner, SceneLightCandidateDiagnosticCode.UnsupportedLightType,
                        $"Light type {light.TypeCode} is present in the source list but has no candidate category implementation."));
                    break;
            }
        }

        return CreateResolution();

        SceneLightCandidateResolution CreateResolution() => new(
            Array.AsReadOnly(diffuse.ToArray()),
            Array.AsReadOnly(fog.ToArray()),
            Array.AsReadOnly(additionalMaterial.ToArray()),
            Array.AsReadOnly(unsupported.ToArray()),
            Array.AsReadOnly(diagnostics.ToArray()));
    }

    private static bool IsInScope(SceneObject owner, SceneLightCandidateScope scope,
        out SceneLightCandidateDiagnosticCode? diagnostic)
    {
        diagnostic = null;
        if (!scope.SectorizationEnabled)
            return true;

        if (scope.MembershipByObject == null
            || !scope.MembershipByObject.TryGetValue(owner, out SceneLightSectorMembership membership)
            || membership == SceneLightSectorMembership.Unknown)
        {
            diagnostic = SceneLightCandidateDiagnosticCode.UnknownSectorMembership;
            return false;
        }

        if (membership is SceneLightSectorMembership.GlobalWorld or SceneLightSectorMembership.ActiveSector)
            return true;

        diagnostic = SceneLightCandidateDiagnosticCode.OutsideActiveSector;
        return false;
    }

    private static string SectorDetail(SceneLightCandidateDiagnosticCode code) => code switch
    {
        SceneLightCandidateDiagnosticCode.UnknownSectorMembership =>
            "The source light's active sector is unknown; it is excluded instead of treating all world lights as active.",
        _ => "The source light is outside the current world/sector light list."
    };

    private static bool IntersectsSphereAabb(Vector3 center, float radius, Vector3 minimum, Vector3 maximum)
    {
        if (!float.IsFinite(radius) || radius < 0)
            return false;

        return AxisGap(center.X, minimum.X, maximum.X) <= radius
            && AxisGap(center.Y, minimum.Y, maximum.Y) <= radius
            && AxisGap(center.Z, minimum.Z, maximum.Z) <= radius;
    }

    private static float AxisGap(float value, float minimum, float maximum) =>
        value < minimum ? minimum - value : value > maximum ? value - maximum : 0;
}
