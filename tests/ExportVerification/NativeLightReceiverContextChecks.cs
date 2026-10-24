using JadeView;

internal static class NativeLightReceiverContextChecks
{
    private const uint DynamicReceiverBit = 0x8;

    internal static void Verify()
    {
        VerifyNativeStateMaskAndDynamicStatus();
        VerifyInitialActorResolution();
        VerifyBoneHierarchyDiagnostics();
        Console.WriteLine("PASS: native light receiver flags2 and initial actor reference reconstruction");
    }

    private static void VerifyNativeStateMaskAndDynamicStatus()
    {
        SceneObject visual = Object(0x100, flags: 0x4000);
        SceneObject animated = Object(0x101, flags: 0x2);
        NativeLightReceiverContext staticContext = Resolve(visual);
        NativeLightReceiverContext dynamicByIdentity = Resolve(animated);
        NativeLightReceiverContext dynamicByState = Resolve(visual, [visual], 0x00040000);
        NativeLightReceiverContext forcedStatic = Resolve(animated, [animated], 0x00080000);
        NativeLightReceiverContext mappedState = Resolve(visual, [visual],
            0x20000000 | 0x40000000 | 0x0000A500);
        NativeLightReceiverContext overrideState = Resolve(animated, [animated], 0x00080000);

        Require(staticContext.IsResolved && (staticContext.RuntimeFlags2 & DynamicReceiverBit) == 0,
            "An eligible visual with no dynamic identity/state bits must remain a static receiver.");
        Require((dynamicByIdentity.RuntimeFlags2 & DynamicReceiverBit) != 0,
            "An animated identity with status-relevant identity bit 0x2 must set runtime flags2 bit 3.");
        Require((dynamicByState.RuntimeFlags2 & DynamicReceiverBit) != 0,
            "Serialized runtime-state bit 0x40000 must set dynamic receiver status for eligible identities.");
        Require((forcedStatic.RuntimeFlags2 & DynamicReceiverBit) == 0,
            "Serialized state bit 0x80000 must clear dynamic receiver status after other inputs.");
        Require((mappedState.RuntimeFlags2 & 0x0060A500) == 0x0060A500,
            "The GAO load mask must preserve allowed raw state bits and map serialized 0x20000000/0x40000000.");
        Require((overrideState.RuntimeFlags2 & DynamicReceiverBit) == 0,
            "An explicit serialized-state override must replace the SceneObject baseline before native status derivation.");
    }

    private static void VerifyInitialActorResolution()
    {
        SceneObject root = Object(0x200, flags: 0x2);
        SceneObject bone = Object(0x201, flags: SceneLoader.IdentityFlagBone, parentId: "00000200");
        SceneObject nestedBone = Object(0x202, flags: SceneLoader.IdentityFlagBone, parentId: "00000201");
        SceneObject[] members = [root, bone, nestedBone];

        NativeLightReceiverContext rootContext = Resolve(root, members);
        NativeLightReceiverContext nestedContext = Resolve(nestedBone, members);
        Require(rootContext.IsResolved && rootContext.InitialActorKey == root.GaoKey,
            "A non-bone receiver is its own initial actor reference.");
        Require(nestedContext.IsResolved && nestedContext.InitialActorKey == root.GaoKey,
            "Bone hierarchy traversal must stop at the first non-bone ancestor, matching ANI_pst_GetReferenceInit.");
    }

    private static void VerifyBoneHierarchyDiagnostics()
    {
        SceneObject missingParent = Object(0x301, flags: SceneLoader.IdentityFlagBone, parentId: "00000399");
        SceneObject cycleA = Object(0x302, flags: SceneLoader.IdentityFlagBone, parentId: "00000303");
        SceneObject cycleB = Object(0x303, flags: SceneLoader.IdentityFlagBone, parentId: "00000302");
        SceneObject ambiguousBone = Object(0x304, flags: SceneLoader.IdentityFlagBone, parentId: "00000305");
        SceneObject duplicateParent1 = Object(0x305, flags: 0x2);
        SceneObject duplicateParent2 = Object(0x305, flags: 0x2);

        NativeLightReceiverContext missing = Resolve(missingParent, [missingParent]);
        NativeLightReceiverContext cycle = Resolve(cycleA, [cycleA, cycleB]);
        NativeLightReceiverContext ambiguous = Resolve(ambiguousBone,
            [ambiguousBone, duplicateParent1, duplicateParent2]);

        Require(missing.Diagnostic == NativeLightReceiverDiagnostic.MissingBoneParent,
            "An unresolved bone parent must be reported rather than treating the bone as its own actor.");
        Require(cycle.Diagnostic == NativeLightReceiverDiagnostic.BoneHierarchyCycle,
            "A cyclic bone hierarchy must terminate with an explicit diagnostic.");
        Require(ambiguous.Diagnostic == NativeLightReceiverDiagnostic.AmbiguousBoneParent,
            "Duplicate parent keys cannot be silently collapsed to an arbitrary actor reference.");
    }

    private static NativeLightReceiverContext Resolve(SceneObject receiver,
        IReadOnlyCollection<SceneObject>? objects = null,
        uint? stateOverride = null) =>
        NativeLightReceiverContextResolver.Resolve(receiver, objects ?? [receiver], stateOverride);

    private static SceneObject Object(uint key, uint flags, string? parentId = null,
        uint state = 0) => new()
    {
        GaoKey = key,
        Flags = flags,
        ParentId = parentId,
        SerializedStateFlags = state
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
