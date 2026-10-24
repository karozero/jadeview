namespace JadeView;

internal enum NativeLightReceiverDiagnostic
{
    None,
    MissingBoneParent,
    InvalidBoneParentKey,
    AmbiguousBoneParent,
    BoneHierarchyCycle
}

internal readonly record struct NativeLightReceiverContext(
    uint RuntimeFlags2,
    uint InitialActorKey,
    NativeLightReceiverDiagnostic Diagnostic)
{
    internal bool IsResolved => Diagnostic == NativeLightReceiverDiagnostic.None;
}

/// <summary>
/// Reconstructs the PC engine's initial light receiver state from serialized GAO fields.
/// This models object initialization only; script/runtime mutations must be passed as an
/// explicit serialized-state override or applied by a future runtime-state layer.
/// </summary>
internal static class NativeLightReceiverContextResolver
{
    private const uint RuntimeFlags2LoadMask = 0x7B9C0080;
    private const uint SerializedDynamicState = 0x20000000;
    private const uint SerializedCollisionState = 0x40000000;
    private const uint DynamicStatus = 0x00200000;
    private const uint CollisionStatus = 0x00400000;
    private const uint DynamicReceiverStatus = 0x00040000;
    private const uint ForceStaticReceiverStatus = 0x00080000;
    private const uint DynamicReceiverFlag = 0x00000008;
    private const uint BoneIdentityFlag = 0x00000001;
    private const uint StatusRelevantIdentityFlags = 0x00000026;
    private const uint StatusEligibleIdentityFlags = 0x00004002;

    internal static NativeLightReceiverContext Resolve(
        SceneObject receiver,
        IReadOnlyCollection<SceneObject> loadedObjects,
        uint? serializedStateFlagsOverride = null)
    {
        ArgumentNullException.ThrowIfNull(receiver);
        ArgumentNullException.ThrowIfNull(loadedObjects);

        uint serializedStateFlags = serializedStateFlagsOverride ?? receiver.SerializedStateFlags;
        uint runtimeFlags2 = serializedStateFlags & RuntimeFlags2LoadMask;
        if ((serializedStateFlags & SerializedDynamicState) != 0)
            runtimeFlags2 |= DynamicStatus;
        if ((serializedStateFlags & SerializedCollisionState) != 0)
            runtimeFlags2 |= CollisionStatus;
        runtimeFlags2 |= serializedStateFlags & 0x0000FF00;

        runtimeFlags2 = UpdateStatusFlagRtl(receiver.Flags, runtimeFlags2);
        NativeLightReceiverDiagnostic parentDiagnostic = ResolveInitialActorKey(
            receiver, loadedObjects, out uint initialActorKey);
        return new(runtimeFlags2, initialActorKey, parentDiagnostic);
    }

    private static uint UpdateStatusFlagRtl(uint identityFlags, uint runtimeFlags2)
    {
        // Iris OBJ_UpdateStatusFlagRTL (gameobject.c:47-70): only animated/visual identities
        // can become dynamic receivers; then identity flags or state bit 0x40000 set bit 3,
        // while state bit 0x80000 forces it off.
        if ((identityFlags & StatusEligibleIdentityFlags) == 0)
            return runtimeFlags2 & ~DynamicReceiverFlag;

        bool dynamicByIdentity = (identityFlags & StatusRelevantIdentityFlags) != 0;
        bool dynamicByState = (runtimeFlags2 & DynamicReceiverStatus) != 0;
        bool forceStatic = (runtimeFlags2 & ForceStaticReceiverStatus) != 0;
        bool isDynamic = (dynamicByIdentity || dynamicByState) && !forceStatic;
        return isDynamic
            ? runtimeFlags2 | DynamicReceiverFlag
            : runtimeFlags2 & ~DynamicReceiverFlag;
    }

    private static NativeLightReceiverDiagnostic ResolveInitialActorKey(
        SceneObject receiver,
        IReadOnlyCollection<SceneObject> loadedObjects,
        out uint initialActorKey)
    {
        var visited = new HashSet<SceneObject>(ReferenceEqualityComparer.Instance);
        SceneObject current = receiver;
        while (true)
        {
            if (!visited.Add(current))
            {
                initialActorKey = 0;
                return NativeLightReceiverDiagnostic.BoneHierarchyCycle;
            }

            if ((current.Flags & BoneIdentityFlag) == 0)
            {
                initialActorKey = current.GaoKey;
                return NativeLightReceiverDiagnostic.None;
            }

            if (string.IsNullOrWhiteSpace(current.ParentId))
            {
                initialActorKey = 0;
                return NativeLightReceiverDiagnostic.MissingBoneParent;
            }

            if (!uint.TryParse(current.ParentId, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out uint parentKey))
            {
                initialActorKey = 0;
                return NativeLightReceiverDiagnostic.InvalidBoneParentKey;
            }

            SceneObject[] parents = loadedObjects.Where(candidate => candidate.GaoKey == parentKey).ToArray();
            if (parents.Length == 0)
            {
                initialActorKey = 0;
                return NativeLightReceiverDiagnostic.MissingBoneParent;
            }

            if (parents.Length > 1)
            {
                initialActorKey = 0;
                return NativeLightReceiverDiagnostic.AmbiguousBoneParent;
            }

            current = parents[0];
        }
    }
}
