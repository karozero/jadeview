using JadeView;
using System.Numerics;

internal static class SceneLightCandidateResolverChecks
{
    private const uint LightIdentityFlag = 0x10;
    private const uint LightedDrawMask = 0x08000000;
    private const uint CheckBoundsDrawMask = 0x00200000;
    private const uint DynamicObjectFlag = 0x8;
    private const uint DynamicReceiverFlag = 0x100;
    private const uint StaticReceiverFlag = 0x200;
    private const uint ActiveFlag = 0x8;

    internal static void Verify()
    {
        VerifySelectedWorldAndSourceOrder();
        VerifyPerPlayheadLightStateAndSourceFilters();
        VerifyReceiverReferenceAndBoundsGates();
        VerifyNativeAabbRangeGateAndTypedBoundsLimits();
        VerifySectorScopeRequiresExplicitMembership();
        Console.WriteLine("PASS: scene light candidates use selected-world membership, playhead state, source gates and explicit sector/bounds scope");
    }

    private static void VerifySelectedWorldAndSourceOrder()
    {
        SceneObject firstOmni = LightObject(0x101, SceneLightType.Omni);
        SceneObject fog = LightObject(0x102, SceneLightType.Fog);
        SceneObject additionalMaterial = LightObject(0x103, SceneLightType.AddMaterial);
        SceneObject unknownType = LightObject(0x104, typeCode: 5);
        SceneObject objectWithUnbackedLight = new() { GaoKey = 0x105, Name = "not-a-light" };
        objectWithUnbackedLight.Light = CreateLight(SceneLightType.Direct);
        SceneObject secondOmni = LightObject(0x106, SceneLightType.Omni);

        WorldData selectedWorld = World("selected", firstOmni, fog, additionalMaterial,
            unknownType, objectWithUnbackedLight, secondOmni);
        WorldData unrelatedLoadedWorld = World("unrelated", LightObject(0x201, SceneLightType.Direct));
        SceneLightCandidateResolution result = Resolve(selectedWorld);

        Require(result.Diffuse.Select(candidate => candidate.Owner).SequenceEqual([firstOmni, secondOmni]),
            "Diffuse candidates must preserve selected-world order and include only parsed identity-light objects.");
        Require(result.Fog.Select(candidate => candidate.Owner).SequenceEqual([fog]),
            "Active fog lights must be routed away from point-diffuse candidates.");
        Require(result.AdditionalMaterial.Select(candidate => candidate.Owner).SequenceEqual([additionalMaterial]),
            "Type-4 lights must remain separate from point-diffuse evaluation.");
        Require(result.Unsupported.Select(candidate => candidate.Owner).SequenceEqual([unknownType]),
            "Unrecognized active light types must remain visible as unsupported, not silently discarded.");
        Require(!result.Diffuse.Any(candidate => candidate.Owner == unrelatedLoadedWorld.Objects[0]),
            "Candidate discovery must use the explicitly selected world, not every loaded document world.");
    }

    private static void VerifyPerPlayheadLightStateAndSourceFilters()
    {
        SceneObject activeAtBaseline = LightObject(0x301, SceneLightType.Direct);
        SceneObject disabledAtBaseline = LightObject(0x302, SceneLightType.Direct, flags: 0);
        SceneObject shadowBufferLight = LightObject(0x303, SceneLightType.Direct,
            flags: ActiveFlag | DynamicReceiverFlag | 0x40000000);
        WorldData selectedWorld = World("cinematic", activeAtBaseline, disabledAtBaseline, shadowBufferLight);
        CinematicLightStateEvaluation evaluation = new([
            new(activeAtBaseline, CreateLight(SceneLightType.Direct, flags: 0)),
            new(disabledAtBaseline, CreateLight(SceneLightType.Direct,
                flags: ActiveFlag | DynamicReceiverFlag))
        ], []);

        SceneLightCandidateResolution result = Resolve(selectedWorld, evaluation);
        Require(result.Diffuse.Count == 1 && result.Diffuse[0].Owner == disabledAtBaseline,
            "The evaluator must use per-playhead component flags over serialized baseline flags.");
        Require(result.Diagnostics.Any(item => item.Owner == activeAtBaseline && item.Code == SceneLightCandidateDiagnosticCode.Inactive),
            "An EVE-updated inactive light must be diagnosed and excluded.");
        Require(result.Diagnostics.Any(item => item.Owner == shadowBufferLight && item.Code == SceneLightCandidateDiagnosticCode.ShadowBufferOwned),
            "Desktop shadow-buffer-only lights must not enter the ordinary dynamic-diffuse list.");
    }

    private static void VerifyReceiverReferenceAndBoundsGates()
    {
        SceneObject dynamicOnly = LightObject(0x401, SceneLightType.Spot, flags: ActiveFlag | DynamicReceiverFlag);
        SceneObject referencedOtherActor = LightObject(0x402, SceneLightType.Omni,
            flags: ActiveFlag | DynamicReceiverFlag, reference: 0x999);
        WorldData selectedWorld = World("receiver", dynamicOnly, referencedOtherActor);

        SceneLightCandidateResolution staticReceiver = Resolve(selectedWorld, receiverFlags2: 0,
            initialActorKey: 0x999);
        Require(staticReceiver.Diffuse.Count == 0
            && staticReceiver.Diagnostics.Any(item => item.Code == SceneLightCandidateDiagnosticCode.ReceiverMaskRejected),
            "Static receivers must require the static-light bit.");

        SceneLightCandidateResolution dynamicReceiver = Resolve(selectedWorld, receiverFlags2: DynamicObjectFlag,
            initialActorKey: 0x401);
        Require(dynamicReceiver.Diffuse.Count == 1 && dynamicReceiver.Diffuse[0].Owner == dynamicOnly
            && dynamicReceiver.Diagnostics.Any(item => item.Owner == referencedOtherActor
                && item.Code == SceneLightCandidateDiagnosticCode.LightReferenceMismatch),
            "Dynamic receiver masks and referenced initial-actor gates must be applied per candidate.");

        SceneLightCandidateResolution unknownBounds = Resolve(World("bounds", LightObject(0x403, SceneLightType.Omni)),
            receiverDrawMask: LightedDrawMask | CheckBoundsDrawMask);
        Require(unknownBounds.Diffuse.Count == 0
            && unknownBounds.Diagnostics.Any(item => item.Code == SceneLightCandidateDiagnosticCode.ReceiverBoundsTestUnavailable),
            "A requested source light-radius/AABB test must not silently become an all-world candidate pass when bounds are missing.");

        SceneLightCandidateResolution unlitReceiver = Resolve(World("unlit", dynamicOnly,
            LightObject(0x406, SceneLightType.Fog)), receiverDrawMask: 0);
        Require(unlitReceiver.Diffuse.Count == 0
            && unlitReceiver.Fog.Count == 1
            && unlitReceiver.Diagnostics.Any(item => item.Code == SceneLightCandidateDiagnosticCode.ReceiverNotLighted),
            "The receiver Lighted draw-mask bit gates per-object candidates while global fog remains available.");
    }

    private static void VerifyNativeAabbRangeGateAndTypedBoundsLimits()
    {
        SceneObject outside = LightObject(0x430, SceneLightType.Omni);
        SceneObject grazing = LightObject(0x431, SceneLightType.Spot);
        SceneObject inside = LightObject(0x432, SceneLightType.Omni);
        WorldData world = World("range", outside, grazing, inside);
        PcObjectBounds aabb = new(PcObjectBoundsKind.AxisAlignedBox,
            new(-1, -1, -1), new(1, 1, 1), null, null, null);
        var matrices = new Dictionary<SceneObject, Matrix4x4>
        {
            [outside] = Matrix4x4.CreateTranslation(12, 0, 0),
            [grazing] = Matrix4x4.CreateTranslation(11, 0, 0),
            [inside] = Matrix4x4.CreateTranslation(2, 0, 0)
        };
        SceneLightReceiverBoundsContext aabbContext = new(aabb, Vector3.Zero, matrices);

        SceneLightCandidateResolution ranged = Resolve(world,
            receiverDrawMask: LightedDrawMask | CheckBoundsDrawMask,
            boundsContext: aabbContext);
        Require(ranged.Diffuse.Select(candidate => candidate.Owner).SequenceEqual([grazing, inside]),
            "The AABB radius gate must exclude an outside Omni, accept exact grazing contact, and preserve source order.");
        Require(ranged.Diagnostics.Any(item => item.Owner == outside
                && item.Code == SceneLightCandidateDiagnosticCode.ReceiverOutsideLightRange),
            "A rejected source-order light must expose a typed out-of-range diagnostic.");

        SceneObject animatedLight = LightObject(0x435, SceneLightType.Omni);
        SceneLightCandidateResolution animatedBounds = Resolve(World("animated-bounds", animatedLight),
            receiverDrawMask: LightedDrawMask | CheckBoundsDrawMask,
            boundsContext: new(aabb, Vector3.Zero,
                new Dictionary<SceneObject, Matrix4x4> { [animatedLight] = Matrix4x4.Identity },
                BoundsAreRuntimeDynamic: true));
        Require(animatedBounds.Diffuse.Count == 0
            && animatedBounds.Diagnostics.Any(item => item.Code == SceneLightCandidateDiagnosticCode.ReceiverDynamicBoundsUnavailable),
            "Serialized bounds must not stand in for the game's per-frame skeleton-derived animated bounds.");

        PcObjectBounds sphere = new(PcObjectBoundsKind.Sphere,
            Vector3.Zero, Vector3.Zero, null, null, 2);
        SceneObject sphereLight = LightObject(0x433, SceneLightType.Omni);
        SceneLightCandidateResolution sphereResult = Resolve(World("sphere", sphereLight),
            receiverDrawMask: LightedDrawMask | CheckBoundsDrawMask,
            boundsContext: new(sphere, Vector3.Zero,
                new Dictionary<SceneObject, Matrix4x4> { [sphereLight] = Matrix4x4.Identity }));
        Require(sphereResult.Diffuse.Count == 0
            && sphereResult.Diagnostics.Any(item => item.Code == SceneLightCandidateDiagnosticCode.UnsupportedReceiverBounds),
            "Sphere metadata is retained, but the initial bounded integration must exclude it with a typed diagnostic.");

        PcObjectBounds obb = new(PcObjectBoundsKind.OrientedBox,
            new(-1, -1, -1), new(1, 1, 1), new(100, 100, 100), new(101, 101, 101), null);
        SceneObject obbLight = LightObject(0x434, SceneLightType.Omni);
        SceneLightCandidateResolution obbResult = Resolve(World("obb", obbLight),
            receiverDrawMask: LightedDrawMask | CheckBoundsDrawMask,
            boundsContext: new(obb, new Vector3(5, 0, 0),
                new Dictionary<SceneObject, Matrix4x4> { [obbLight] = Matrix4x4.CreateTranslation(14, 0, 0) }));
        Require(obbResult.Diffuse.Count == 1 && obbResult.Diffuse[0].Owner == obbLight,
            "Static OBB receivers must use the serialized outer AABB for the same native sphere/AABB range test; local OBB corners are not the tested bounds.");

        SceneObject dynamicObbLight = LightObject(0x436, SceneLightType.Omni);
        SceneLightCandidateResolution dynamicObbResult = Resolve(World("dynamic-obb", dynamicObbLight),
            receiverDrawMask: LightedDrawMask | CheckBoundsDrawMask,
            boundsContext: new(obb, Vector3.Zero,
                new Dictionary<SceneObject, Matrix4x4> { [dynamicObbLight] = Matrix4x4.Identity },
                BoundsAreRuntimeDynamic: true));
        Require(dynamicObbResult.Diffuse.Count == 0
            && dynamicObbResult.Diagnostics.Any(item => item.Code == SceneLightCandidateDiagnosticCode.ReceiverDynamicBoundsUnavailable),
            "An OBB whose outer AABB is recomputed from a runtime pose must remain unknown when only serialized bounds are available.");
    }

    private static void VerifySectorScopeRequiresExplicitMembership()
    {
        SceneObject global = LightObject(0x501, SceneLightType.Direct);
        SceneObject activeSector = LightObject(0x502, SceneLightType.Spot);
        SceneObject inactiveSector = LightObject(0x503, SceneLightType.Omni);
        SceneObject unknown = LightObject(0x504, SceneLightType.Direct);
        WorldData selectedWorld = World("sectorized", global, activeSector, inactiveSector, unknown);

        SceneLightCandidateResolution noMembership = Resolve(selectedWorld,
            scope: new SceneLightCandidateScope(true));
        Require(noMembership.Diffuse.Count == 0
            && noMembership.Diagnostics.Any(item => item.Code == SceneLightCandidateDiagnosticCode.UnsupportedSectorScope),
            "Sectorized resolution must stop when no active-sector table is available.");

        var memberships = new Dictionary<SceneObject, SceneLightSectorMembership>
        {
            [global] = SceneLightSectorMembership.GlobalWorld,
            [activeSector] = SceneLightSectorMembership.ActiveSector,
            [inactiveSector] = SceneLightSectorMembership.OutsideActiveSector,
            [unknown] = SceneLightSectorMembership.Unknown
        };
        SceneLightCandidateResolution explicitMembership = Resolve(selectedWorld,
            scope: new SceneLightCandidateScope(true, memberships));
        Require(explicitMembership.Diffuse.Select(candidate => candidate.Owner).SequenceEqual([global, activeSector]),
            "A sectorized source list may include only global and explicitly active-sector light entries.");
        Require(explicitMembership.Diagnostics.Any(item => item.Owner == unknown
                && item.Code == SceneLightCandidateDiagnosticCode.UnknownSectorMembership)
            && explicitMembership.Diagnostics.Any(item => item.Owner == inactiveSector
                && item.Code == SceneLightCandidateDiagnosticCode.OutsideActiveSector),
            "Unknown or inactive sector membership must be explained and excluded.");
    }

    private static SceneLightCandidateResolution Resolve(WorldData world,
        CinematicLightStateEvaluation? state = null,
        uint receiverFlags2 = DynamicObjectFlag,
        uint receiverDrawMask = LightedDrawMask,
        uint initialActorKey = 0x1234,
        SceneLightCandidateScope? scope = null,
        SceneLightReceiverBoundsContext? boundsContext = null) =>
        SceneLightCandidateResolver.Resolve(world, receiverFlags2, receiverDrawMask,
            initialActorKey, state, scope, boundsContext);

    private static WorldData World(string name, params SceneObject[] objects)
    {
        var world = new WorldData { Name = name };
        world.Objects.AddRange(objects);
        return world;
    }

    private static SceneObject LightObject(uint key, SceneLightType type,
        uint flags = ActiveFlag | DynamicReceiverFlag, uint reference = SceneLight.NoLightGaoKey) => new()
    {
        GaoKey = key,
        Name = $"light-{key:X8}",
        Flags = LightIdentityFlag,
        LightResourceKey = key + 0x1000,
        Light = CreateLight(type, flags, reference)
    };

    private static SceneObject LightObject(uint key, uint typeCode,
        uint flags = ActiveFlag | DynamicReceiverFlag) => new()
    {
        GaoKey = key,
        Name = $"light-{key:X8}",
        Flags = LightIdentityFlag,
        LightResourceKey = key + 0x1000,
        Light = new(flags | typeCode, 0xFFFFFFFF, 1, 10, 0.2f, 0.8f, SceneLight.NoLightGaoKey)
    };

    private static SceneLight CreateLight(SceneLightType type,
        uint flags = ActiveFlag | DynamicReceiverFlag,
        uint reference = SceneLight.NoLightGaoKey) =>
        new(flags | (uint)type, 0xFFFFFFFF, 1, 10, 0.2f, 0.8f, reference);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
