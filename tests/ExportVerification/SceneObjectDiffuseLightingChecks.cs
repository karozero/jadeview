using System.Numerics;
using JadeView;

internal static class SceneObjectDiffuseLightingChecks
{
    private const uint DynamicIdentity = 0x00004002;
    private const uint DynamicState = 0x00040000;
    private const uint ReceiverDrawFlags = 0x08000000;

    internal static void Verify()
    {
        VerifyPreparedBatchUsesOrderedLightsAndGameMatrices();
        VerifyAmbientIsAppliedAfterAbsorbingLights();
        VerifyEmptyPlanPreservesCallerPath();
        VerifySignatureTracksPreparedLightState();
        VerifyMultipartReceiverBoundsUseRootAdjustedPosition();
        Console.WriteLine("PASS: shared prepared object diffuse lighting batch, source order, cache signature, and no-light behavior");
    }

    private static void VerifyAmbientIsAppliedAfterAbsorbingLights()
    {
        SceneObject receiver = CreateReceiver(0x1050);
        SceneObject subtractLight = CreateLightOwner(0x2050, 0x00000081, 0x00ffffff);
        WorldData world = new();
        world.Objects.Add(receiver);
        world.Objects.Add(subtractLight);
        var matrices = new Dictionary<SceneObject, Matrix4x4>
        {
            [receiver] = Matrix4x4.Identity,
            [subtractLight] = Matrix4x4.Identity
        };
        uint drawFlags = ReceiverDrawFlags | 0x00000140;
        PreparedSceneObjectDiffuseLighting plan = SceneObjectDiffuseLightingEvaluator.Prepare(
            new(world, receiver, matrices, drawFlags, 0, 0, Vector3.Zero));
        uint[] output = [0];

        SceneObjectDiffuseLightingEvaluator.Evaluate(
            plan, [Vector3.Zero], [Vector3.UnitY], [0x00ffffff], Vector4.Zero, 0,
            new Vector4(0.25f, 0.25f, 0.25f, 0), output);

        Require(output[0] == 0x00404040,
            "Ambient must be added after an absorbing light so light subtraction cannot remove world ambient.");
    }

    private static void VerifyPreparedBatchUsesOrderedLightsAndGameMatrices()
    {
        SceneObject receiver = CreateReceiver(0x1000);
        SceneObject addLight = CreateLightOwner(0x2000, 0x00000001, 0x000000ff);
        SceneObject subtractLight = CreateLightOwner(0x3000, 0x00000081, 0x000000ff);
        WorldData world = new();
        world.Objects.Add(receiver);
        world.Objects.Add(addLight);
        world.Objects.Add(subtractLight);
        var matrices = new Dictionary<SceneObject, Matrix4x4>
        {
            [receiver] = Matrix4x4.Identity,
            [addLight] = Matrix4x4.Identity,
            [subtractLight] = Matrix4x4.Identity
        };

        PreparedSceneObjectDiffuseLighting plan = SceneObjectDiffuseLightingEvaluator.Prepare(
            new(world, receiver, matrices, ReceiverDrawFlags, 0, 0, Vector3.Zero));
        Vector3[] positions = [Vector3.Zero, new(2, 0, 0)];
        Vector3[] normals = [Vector3.UnitY, Vector3.UnitY];
        uint[] rawPaint = [0, 0];
        uint[] output = [0xdeadbeef, 0xdeadbeef];

        bool evaluated = SceneObjectDiffuseLightingEvaluator.Evaluate(
            plan, positions, normals, rawPaint, Vector4.Zero, 0, Vector4.Zero, output);

        Require(evaluated && plan.CandidateCount == 2 && plan.HasModeledContribution,
            "The prepared plan must expose both active source-ordered diffuse candidates.");
        Require(output[0] == 0 && output[1] == 0,
            "Per-vertex evaluation must apply add then absorb in WOL source order and fill every output slot.");
    }

    private static void VerifyEmptyPlanPreservesCallerPath()
    {
        SceneObject receiver = CreateReceiver(0x1100);
        WorldData world = new();
        world.Objects.Add(receiver);
        var matrices = new Dictionary<SceneObject, Matrix4x4> { [receiver] = Matrix4x4.Identity };
        PreparedSceneObjectDiffuseLighting plan = SceneObjectDiffuseLightingEvaluator.Prepare(
            new(world, receiver, matrices, ReceiverDrawFlags, 0, 0, Vector3.Zero));
        uint[] output = [0x12345678];

        bool evaluated = SceneObjectDiffuseLightingEvaluator.Evaluate(
            plan, [Vector3.Zero], [Vector3.UnitY], [0], Vector4.Zero, 0, Vector4.Zero, output);

        Require(!evaluated && plan.CandidateCount == 0 && output[0] == 0x12345678,
            "A plan without supported active diffuse lights must leave output untouched for the renderer's existing path.");
    }

    private static void VerifySignatureTracksPreparedLightState()
    {
        SceneObject receiver = CreateReceiver(0x1200);
        SceneObject lightOwner = CreateLightOwner(0x2200, 0x00000001, 0x00000080);
        WorldData world = new();
        world.Objects.Add(receiver);
        world.Objects.Add(lightOwner);
        var matrices = new Dictionary<SceneObject, Matrix4x4>
        {
            [receiver] = Matrix4x4.Identity,
            [lightOwner] = Matrix4x4.Identity
        };

        ulong originalSignature = SceneObjectDiffuseLightingEvaluator.Prepare(
            new(world, receiver, matrices, ReceiverDrawFlags, 0, 0, Vector3.Zero)).Signature;
        matrices[lightOwner] = Matrix4x4.CreateTranslation(0, 1, 0);
        ulong movedSignature = SceneObjectDiffuseLightingEvaluator.Prepare(
            new(world, receiver, matrices, ReceiverDrawFlags, 0, 0, Vector3.Zero)).Signature;

        Require(originalSignature != movedSignature,
            "A cached plan signature must change when a source light's game-space matrix changes.");
    }

    private static void VerifyMultipartReceiverBoundsUseRootAdjustedPosition()
    {
        SceneObject root = new() { Id = "1000", GaoKey = 0x1000, Name = "actor root" };
        SceneObject receiver = new()
        {
            Id = "1001",
            GaoKey = 0x1001,
            Name = "multipart receiver",
            ParentId = "00001000",
            // Keep the native multipart hierarchy bits but clear the animated identity bit:
            // this fixture exercises static serialized AABB selection, not live skeleton BVs.
            Flags = (DynamicIdentity & ~SceneLoader.IdentityFlagAnims) | 0x00600001,
            SerializedStateFlags = DynamicState,
            PcBounds = new(PcObjectBoundsKind.AxisAlignedBox,
                new(-1, -1, -1), new(1, 1, 1), null, null, null)
        };
        SceneObject light = CreateLightOwner(0x2001, 0, 0x000000ff, typeCode: 0);
        var actor = new SceneActor { Root = root };
        actor.Members.Add(root);
        actor.Members.Add(receiver);
        root.Actor = actor;
        receiver.Actor = actor;
        WorldData world = new();
        world.Objects.AddRange([receiver, root, light]);
        var matrices = new Dictionary<SceneObject, Matrix4x4>
        {
            [receiver] = Matrix4x4.CreateTranslation(100, 0, 0),
            [root] = Matrix4x4.Identity,
            [light] = Matrix4x4.CreateTranslation(109, 0, 0)
        };

        PreparedSceneObjectDiffuseLighting plan = SceneObjectDiffuseLightingEvaluator.Prepare(
            new(world, receiver, matrices, ReceiverDrawFlags | 0x00200000, 0, 0, Vector3.Zero));

        Require(plan.CandidateCount == 1,
            "A bounds-gated multipart actor must test light-relative position after applying its initial actor matrix.");

        matrices[root] = Matrix4x4.CreateTranslation(100, 0, 0);
        PreparedSceneObjectDiffuseLighting movedRootPlan = SceneObjectDiffuseLightingEvaluator.Prepare(
            new(world, receiver, matrices, ReceiverDrawFlags | 0x00200000, 0, 0, Vector3.Zero));
        Require(movedRootPlan.CandidateCount == 0
            && ReferenceEquals(movedRootPlan.BoundsReferenceOwner, root)
            && movedRootPlan.BoundsReferenceMatrix == matrices[root],
            "Changing the root transform must change the source bounds gate instead of reusing the unadjusted child position.");
    }

    private static SceneObject CreateReceiver(uint key) => new()
    {
        Id = key.ToString("X8"),
        GaoKey = key,
        Flags = DynamicIdentity,
        SerializedStateFlags = DynamicState
    };

    private static SceneObject CreateLightOwner(uint key, uint extraFlags, uint color, uint typeCode = 1)
    {
        var light = new SceneLight(
            0x00000008 | 0x00000100 | 0x00000020 | 0x00000040 | typeCode | extraFlags,
            color,
            0,
            10,
            0.2f,
            0.8f,
            SceneLight.NoLightGaoKey);
        return new SceneObject
        {
            Id = key.ToString("X8"),
            GaoKey = key,
            Name = "test light",
            Flags = 0x00000010,
            LightResourceKey = key + 1,
            Light = light
        };
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
