using System.Numerics;
using System.Runtime.CompilerServices;

namespace JadeView;

internal sealed record SceneObjectDiffuseLightingRequest(
    WorldData World,
    SceneObject Receiver,
    IReadOnlyDictionary<SceneObject, Matrix4x4> GameWorldMatrices,
    uint EffectiveDrawFlags,
    byte VisualDistanceAttenuation,
    float DisplayLightAttenuation,
    Vector3 LocalBoundsCenter,
    CinematicLightStateEvaluation? LightState = null,
    SceneLightCandidateScope? Scope = null,
    bool? ReceiverHasBoundingVolume = null,
    SceneLightReceiverBoundsContext? ReceiverBoundsContext = null,
    IReadOnlyList<SceneObject>? ReceiverContextObjects = null);

internal sealed record PreparedSceneObjectDiffuseLighting(
    ulong Signature,
    int CandidateCount,
    uint ReceiverDrawFlags,
    PreparedSceneLightEvaluation[] Lights,
    SceneObjectDiffuseLightingDiagnostic[] Diagnostics)
{
    internal bool HasModeledContribution => CandidateCount != 0;
    internal SceneObject? BoundsReferenceOwner { get; init; }
    internal Matrix4x4 BoundsReferenceMatrix { get; init; }
}

internal sealed record SceneObjectDiffuseLightingDiagnostic(
    SceneObject? LightOwner,
    string Detail,
    SceneLightCandidateDiagnosticCode? CandidateCode = null)
{
    internal bool IsUnsupported => CandidateCode is not (
        SceneLightCandidateDiagnosticCode.ReceiverNotLighted
        or SceneLightCandidateDiagnosticCode.OutsideActiveSector
        or SceneLightCandidateDiagnosticCode.Inactive
        or SceneLightCandidateDiagnosticCode.ShadowBufferOwned
        or SceneLightCandidateDiagnosticCode.ReceiverMaskRejected
        or SceneLightCandidateDiagnosticCode.LightReferenceMismatch
        or SceneLightCandidateDiagnosticCode.ReceiverOutsideLightRange);
}

/// <summary>
/// Prepares source-ordered diffuse lights once for an object, then evaluates a vertex batch
/// without allocations, world scans, or matrix inversion. Inputs use game-space matrices and
/// vertices; activation is supplied only through the captured light-state snapshot.
/// </summary>
internal static class SceneObjectDiffuseLightingEvaluator
{
    internal static PreparedSceneObjectDiffuseLighting Prepare(SceneObjectDiffuseLightingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.World);
        ArgumentNullException.ThrowIfNull(request.Receiver);
        ArgumentNullException.ThrowIfNull(request.GameWorldMatrices);

        var diagnostics = new List<SceneObjectDiffuseLightingDiagnostic>();
        NativeLightReceiverContext receiverContext = NativeLightReceiverContextResolver.Resolve(
            request.Receiver, request.ReceiverContextObjects ?? request.World.Objects);
        if (!receiverContext.IsResolved)
        {
            diagnostics.Add(new(request.Receiver,
                $"Receiver initial-actor context is unresolved ({receiverContext.Diagnostic}); referenced lights may be rejected."));
        }

        if (!request.GameWorldMatrices.TryGetValue(request.Receiver, out Matrix4x4 receiverMatrix))
        {
            diagnostics.Add(new(request.Receiver,
                "Receiver has no current game-space world matrix; diffuse lights were not prepared."));
            return new(0, 0, request.EffectiveDrawFlags, Array.Empty<PreparedSceneLightEvaluation>(),
                diagnostics.ToArray());
        }

        SceneLightReceiverBoundsContext boundsContext = request.ReceiverBoundsContext
            ?? CreateReceiverBoundsContext(request, receiverContext, receiverMatrix);
        SceneLightCandidateResolution resolution = SceneLightCandidateResolver.Resolve(
            request.World,
            receiverContext.RuntimeFlags2,
            request.EffectiveDrawFlags,
            receiverContext.InitialActorKey,
            request.LightState,
            request.Scope,
            boundsContext);
        foreach (SceneLightCandidateDiagnostic diagnostic in resolution.Diagnostics)
            diagnostics.Add(new(diagnostic.Owner, diagnostic.Detail, diagnostic.Code));

        if (!SceneLightEvaluation.TryGetInverseObjectWorld(receiverMatrix, out Matrix4x4 inverseReceiverMatrix))
        {
            diagnostics.Add(new(request.Receiver,
                "Receiver game-space world matrix is non-finite or singular; diffuse lights were not prepared."));
            return new(0, 0, request.EffectiveDrawFlags, Array.Empty<PreparedSceneLightEvaluation>(),
                diagnostics.ToArray());
        }

        var preparedLights = new List<PreparedSceneLightEvaluation>(resolution.Diffuse.Count);
        ulong signature = SeedSignature;
        Add(ref signature, RuntimeHelpers.GetHashCode(request.Receiver));
        Add(ref signature, request.Receiver.GaoKey);
        Add(ref signature, request.EffectiveDrawFlags);
        Add(ref signature, receiverContext.RuntimeFlags2);
        Add(ref signature, receiverContext.InitialActorKey);
        Add(ref signature, request.VisualDistanceAttenuation);
        Add(ref signature, BitConverter.SingleToUInt32Bits(request.DisplayLightAttenuation));
        Add(ref signature, request.Scope?.SectorizationEnabled == true ? 1u : 0u);
        Add(ref signature, receiverMatrix);
        Add(ref signature, boundsContext.WorldPosition);
        Add(ref signature, boundsContext.PositionReferenceOwner == null
            ? 0u
            : (uint)RuntimeHelpers.GetHashCode(boundsContext.PositionReferenceOwner));
        Add(ref signature, boundsContext.PositionReferenceMatrix);
        AddBounds(ref signature, boundsContext.Bounds);
        Add(ref signature, request.LocalBoundsCenter);

        foreach (SceneLightCandidate candidate in resolution.Diffuse)
        {
            if (!request.GameWorldMatrices.TryGetValue(candidate.Owner, out Matrix4x4 lightMatrix))
            {
                diagnostics.Add(new(candidate.Owner,
                    "Diffuse light has no current game-space world matrix; candidate was excluded."));
                continue;
            }

            var input = new SceneLightEvaluationInput(
                candidate.Light,
                lightMatrix,
                receiverMatrix,
                Vector3.Zero,
                Vector3.Zero,
                request.LocalBoundsCenter,
                receiverContext.RuntimeFlags2,
                request.EffectiveDrawFlags,
                receiverContext.InitialActorKey,
                request.VisualDistanceAttenuation,
                request.DisplayLightAttenuation,
                0);

            if (!SceneLightEvaluation.TryPrepare(input, inverseReceiverMatrix, out PreparedSceneLightEvaluation prepared,
                    out SceneLightEvaluationStatus status))
            {
                diagnostics.Add(new(candidate.Owner,
                    $"Diffuse light was excluded during preparation ({status})."));
                continue;
            }

            preparedLights.Add(prepared);
            Add(ref signature, RuntimeHelpers.GetHashCode(candidate.Owner));
            Add(ref signature, candidate.Owner.GaoKey);
            Add(ref signature, candidate.Light.Flags);
            Add(ref signature, candidate.Light.Color);
            Add(ref signature, BitConverter.SingleToUInt32Bits(candidate.Light.Near));
            Add(ref signature, BitConverter.SingleToUInt32Bits(candidate.Light.Far));
            Add(ref signature, BitConverter.SingleToUInt32Bits(candidate.Light.Density));
            Add(ref signature, BitConverter.SingleToUInt32Bits(candidate.Light.I8));
            Add(ref signature, candidate.Light.LightGaoKey);
            Add(ref signature, lightMatrix);
        }

        PreparedSceneLightEvaluation[] lightArray = preparedLights.ToArray();
        return new(signature, lightArray.Length, request.EffectiveDrawFlags, lightArray,
            diagnostics.ToArray())
        {
            BoundsReferenceOwner = boundsContext.PositionReferenceOwner,
            BoundsReferenceMatrix = boundsContext.PositionReferenceMatrix
        };
    }

    /// <summary>
    /// Fills packed computed colors for a vertex batch. The caller owns and may reuse output.
    /// Returns false for an empty plan so renderers can preserve their current no-light path.
    /// </summary>
    internal static bool Evaluate(
        PreparedSceneObjectDiffuseLighting prepared,
        ReadOnlySpan<Vector3> localPositions,
        ReadOnlySpan<Vector3> localNormals,
        ReadOnlySpan<uint> rawPaint,
        Vector4 rliDestination,
        float rliFactor,
        Vector4 ambient,
        Span<uint> output)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (localPositions.Length != localNormals.Length
            || localPositions.Length != rawPaint.Length
            || localPositions.Length != output.Length)
        {
            throw new ArgumentException("Position, normal, raw-paint, and output spans must have equal lengths.");
        }

        if (!prepared.HasModeledContribution)
            return false;

        for (int vertexIndex = 0; vertexIndex < localPositions.Length; vertexIndex++)
        {
            uint color = ComputedVertexColorEvaluator.Evaluate(new ComputedVertexColorInput(
                rawPaint[vertexIndex],
                prepared.ReceiverDrawFlags,
                rliDestination,
                rliFactor,
                Vector4.Zero)).ComputedPackedColor;

            for (int lightIndex = 0; lightIndex < prepared.Lights.Length; lightIndex++)
            {
                color = SceneLightEvaluation.EvaluatePrepared(prepared.Lights[lightIndex], localPositions[vertexIndex],
                    localNormals[vertexIndex], color).AccumulatedColor;
            }

            if ((prepared.ReceiverDrawFlags & 0x00000100) != 0)
                color = ComputedVertexColorEvaluator.ApplyAmbient(color, ambient);

            output[vertexIndex] = color;
        }

        return true;
    }

    private const ulong SeedSignature = 14695981039346656037UL;
    private const ulong SignaturePrime = 1099511628211UL;

    private static SceneLightReceiverBoundsContext CreateReceiverBoundsContext(
        SceneObjectDiffuseLightingRequest request,
        NativeLightReceiverContext receiverContext,
        Matrix4x4 receiverMatrix)
    {
        Vector3 receiverPosition = receiverMatrix.Translation;
        bool boundsAreRuntimeDynamic = (request.Receiver.Flags & SceneLoader.IdentityFlagAnims) != 0;
        bool sourceUsesMultipartRootPosition = (request.Receiver.Flags & 0x00600001) == 0x00600001;
        if (!sourceUsesMultipartRootPosition)
        {
            return new(request.Receiver.PcBounds, receiverPosition, request.GameWorldMatrices,
                BoundsAreRuntimeDynamic: boundsAreRuntimeDynamic);
        }

        if (request.Receiver.Actor == null)
        {
            return new(request.Receiver.PcBounds, receiverPosition, request.GameWorldMatrices,
                "The receiver has the native multipart identity flags, but its actor membership is unresolved.",
                BoundsAreRuntimeDynamic: boundsAreRuntimeDynamic);
        }

        if (request.Receiver.Actor.Members.Count <= 1)
        {
            return new(request.Receiver.PcBounds, receiverPosition, request.GameWorldMatrices,
                BoundsAreRuntimeDynamic: boundsAreRuntimeDynamic);
        }

        SceneObject[] roots = (request.ReceiverContextObjects ?? request.World.Objects)
            .Where(candidate => candidate.GaoKey == receiverContext.InitialActorKey)
            .ToArray();
        if (roots.Length != 1 || !request.GameWorldMatrices.TryGetValue(roots[0], out Matrix4x4 rootMatrix))
        {
            return new(request.Receiver.PcBounds, receiverPosition, request.GameWorldMatrices,
                "The native multipart root position cannot be resolved uniquely in the current world matrices.",
                BoundsAreRuntimeDynamic: boundsAreRuntimeDynamic);
        }

        // Iris and iristool transform the receiver position through its initial actor matrix
        // before subtracting it from the light position in the BV range test.
        receiverPosition = Vector3.Transform(receiverPosition, rootMatrix);
        if (!float.IsFinite(receiverPosition.X) || !float.IsFinite(receiverPosition.Y)
            || !float.IsFinite(receiverPosition.Z))
        {
            return new(request.Receiver.PcBounds, default, request.GameWorldMatrices,
                "The native multipart root transform produced a non-finite receiver position.",
                BoundsAreRuntimeDynamic: boundsAreRuntimeDynamic);
        }

        return new(request.Receiver.PcBounds, receiverPosition, request.GameWorldMatrices,
            PositionReferenceOwner: roots[0], PositionReferenceMatrix: rootMatrix,
            BoundsAreRuntimeDynamic: boundsAreRuntimeDynamic);
    }

    private static void AddBounds(ref ulong signature, PcObjectBounds? bounds)
    {
        if (bounds == null)
        {
            Add(ref signature, 0u);
            return;
        }

        Add(ref signature, (uint)bounds.Kind + 1);
        Add(ref signature, bounds.Minimum);
        Add(ref signature, bounds.Maximum);
        Add(ref signature, bounds.LocalMinimum ?? Vector3.Zero);
        Add(ref signature, bounds.LocalMaximum ?? Vector3.Zero);
        Add(ref signature, bounds.SphereRadius is float radius ? BitConverter.SingleToUInt32Bits(radius) : 0u);
    }

    private static void Add(ref ulong signature, uint value)
    {
        signature ^= value;
        signature *= SignaturePrime;
    }

    private static void Add(ref ulong signature, int value) => Add(ref signature, unchecked((uint)value));

    private static void Add(ref ulong signature, Vector3 value)
    {
        Add(ref signature, BitConverter.SingleToUInt32Bits(value.X));
        Add(ref signature, BitConverter.SingleToUInt32Bits(value.Y));
        Add(ref signature, BitConverter.SingleToUInt32Bits(value.Z));
    }

    private static void Add(ref ulong signature, Matrix4x4 value)
    {
        Add(ref signature, BitConverter.SingleToUInt32Bits(value.M11)); Add(ref signature, BitConverter.SingleToUInt32Bits(value.M12));
        Add(ref signature, BitConverter.SingleToUInt32Bits(value.M13)); Add(ref signature, BitConverter.SingleToUInt32Bits(value.M14));
        Add(ref signature, BitConverter.SingleToUInt32Bits(value.M21)); Add(ref signature, BitConverter.SingleToUInt32Bits(value.M22));
        Add(ref signature, BitConverter.SingleToUInt32Bits(value.M23)); Add(ref signature, BitConverter.SingleToUInt32Bits(value.M24));
        Add(ref signature, BitConverter.SingleToUInt32Bits(value.M31)); Add(ref signature, BitConverter.SingleToUInt32Bits(value.M32));
        Add(ref signature, BitConverter.SingleToUInt32Bits(value.M33)); Add(ref signature, BitConverter.SingleToUInt32Bits(value.M34));
        Add(ref signature, BitConverter.SingleToUInt32Bits(value.M41)); Add(ref signature, BitConverter.SingleToUInt32Bits(value.M42));
        Add(ref signature, BitConverter.SingleToUInt32Bits(value.M43)); Add(ref signature, BitConverter.SingleToUInt32Bits(value.M44));
    }
}
