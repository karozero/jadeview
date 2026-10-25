using JadeView;

internal static class CinematicScriptLocalAlphaChecks
{
    internal static void Verify()
    {
        VerifyCallLocalValueAssignments();

        const uint ownerKey = 0x10004AE8;
        const uint worldKey = 0xFF0045A2;
        const uint rootMaterialKey = 0x47000001;
        const uint childMaterialKey = 0x47000002;
        const string callbackName = "CV_aircraft_CV_exec_after_display";

        if (!CinematicScriptLocalAlphaPlayback.TryParseNumber("0xFF", out double hexadecimalAlpha)
            || hexadecimalAlpha != byte.MaxValue)
            throw new InvalidDataException("Hexadecimal callback alpha values must retain hexadecimal F digits.");

        var document = new SceneDocument();
        document.CinematicScriptOwners[new(worldKey, ownerKey)] = new(ownerKey,
            0x3D002D78, 0x3D002D5B, worldKey, 0, 0, "local alpha callback",
            [callbackName], [], [])
        {
            ModelFunctionNames = [callbackName]
        };
        var world = new WorldData { Key = worldKey };
        world.Objects.Add(new SceneObject { GaoKey = ownerKey, MaterialKey = rootMaterialKey });
        byte[] materialBytes = CreateMaterialBytes();
        var materials = new Dictionary<uint, ResolvedMaterial>
        {
            [rootMaterialKey] = new() { Id = new(rootMaterialKey), Type = 4 },
            [childMaterialKey] = new()
            {
                Id = new(childMaterialKey), Type = 5, SourceBytes = materialBytes
            }
        };
        materials[rootMaterialKey].SubMaterialKeys.Add(childMaterialKey);

        CinematicScriptLocalAlphaPlayback playback =
            CinematicScriptLocalAlphaPlayback.Create(document, world, materials, [])
            ?? throw new InvalidDataException("The source-indexed SetLocalAlpha callback was not discovered.");
        IReadOnlyDictionary<(uint Material, int Level), byte> alphas = playback.Evaluate(.5f, null);
        if (!alphas.TryGetValue((childMaterialKey, 0), out byte alpha) || alpha != 255)
            throw new InvalidDataException("The callback's literal SetLocalAlpha value did not reach its resolved child material.");

        var materialState = new CinematicMaterialState(new AnimationClip(), world, materials);
        materialState.Evaluate(.5f);
        materialState.SetScriptMaterialAlphas(alphas);
        JadeMaterialLayer original = JadeMaterialLayer.Read(materialBytes)[0];
        if ((materialState.Apply(childMaterialKey, 0, original).Custom >> 11) != 31
            || (materialState.CaptureLayers().Apply(childMaterialKey, ownerKey, 0, 0, original).Custom >> 11) != 31)
            throw new InvalidDataException("The callback local alpha was not applied to preview and export material snapshots.");

        (float Time, CinematicLiteralCall Call)[] laterTimelineWrite =
        [(.25f, new(ownerKey, 15008, [0, 0, 64]))];
        CinematicScriptLocalAlphaPlayback timelinePrecedence =
            CinematicScriptLocalAlphaPlayback.Create(document, world, materials,
                laterTimelineWrite)
            ?? throw new InvalidDataException("The callback was lost when a timeline alpha write was present.");
        if (timelinePrecedence.Evaluate(.5f, null).ContainsKey((childMaterialKey, 0)))
            throw new InvalidDataException("An explicit later EVE SetLocalAlpha write must take precedence over callback state.");

        Console.WriteLine("PASS: Iris SetLocalAlpha callback resolves material/layer, reaches preview and export snapshots, and yields to later EVE alpha writes");
    }

    private static void VerifyCallLocalValueAssignments()
    {
        const string functionName = "callback_local_alpha";
        const string alphaExpression = "255.0f * factor";
        var owner = new CinematicScriptOwner(0x100, 0x200, 0x300, 0x400,
            0, 0, "callback assignment fixture", [], [],
            [new("enabled", 0, "instance", "1")]);
        var ownerObject = new SceneObject { GaoKey = owner.OwnerKey };
        CinematicScriptBranchConditionSource factorGuard = new("if", "enabled", 1);
        CinematicScriptArgumentAssignmentSource[] sourceAssignments =
        [
            new("factor", "0.5f", 1, 0, BranchConditions: [factorGuard]),
            new("unresolved", "1.0f", 2, 0, IsUnique: false),
            new("alpha", alphaExpression, 3, 0)
        ];
        var call = new CinematicScriptReachableEngineCall(functionName,
            "SetLocalAlpha", "C", ["AI_gpst_CurrentGameObject", "0", "0", alphaExpression],
            sourceAssignments, [], [], [], "fixture.c", 4, functionName);

        CinematicScriptRliValueFlowAssignment[] callAssignments =
            CinematicScriptLocalAlphaPlayback.ValueFlowAssignmentsForCall(call, []);
        if (callAssignments.Any(assignment => assignment.Variable == "unresolved"))
            throw new InvalidDataException("Non-unique call-local definitions must not be used to calculate alpha.");
        if (callAssignments.Single(assignment => assignment.Variable == "factor")
                .BranchConditions.Single() != factorGuard)
            throw new InvalidDataException("Call-local definitions must preserve the source branch that guards their assignment.");
        if (!CinematicScriptScalarValueFlowEvaluator.TryEvaluateFunction(owner, ownerObject,
                functionName, callAssignments, [], 0, [], _ => null, null, null,
                out Dictionary<string, double?> values)
            || !CinematicScriptScalarValueFlowEvaluator.TryEvaluateSourceExpression(
                alphaExpression, values, out double resolvedAlpha)
            || Math.Abs(resolvedAlpha - 127.5) > .0001)
            throw new InvalidDataException("Unique source assignments from the engine-call index must flow into the callback alpha expression.");

        CinematicScriptOwner unknownOwner = owner with
        {
            Variables = [new("enabled", 0, "instance", "unknown")]
        };
        bool guessedUnknownBranch = CinematicScriptScalarValueFlowEvaluator.TryEvaluateFunction(
            unknownOwner, ownerObject, functionName, callAssignments, [], 0, [], _ => null,
            null, null, out Dictionary<string, double?> unknownValues)
            && CinematicScriptScalarValueFlowEvaluator.TryEvaluateSourceExpression(
                alphaExpression, unknownValues, out _);
        if (guessedUnknownBranch)
            throw new InvalidDataException("A call-local alpha expression must stay unresolved when its reaching assignment is behind an unknown branch.");

        VerifyIndexedCurrentPositionAlpha();
    }

    private static void VerifyIndexedCurrentPositionAlpha()
    {
        const uint ownerKey = 0x10004AE8;
        const uint worldKey = 0xFF0045A2;
        const uint materialKey = 0x47000003;
        const string callbackName = "CV_aircraft_CV_exec_before_display";
        var document = new SceneDocument();
        document.CinematicScriptOwners[new(worldKey, ownerKey)] = new(ownerKey,
            0x3D000001, 0x3D000002, worldKey, 0, 0, "aircraft fade callback",
            [callbackName], [],
            [new("i_flag_on_coule", 0, "instance", "1"),
             new("f_water_Z", 0, "instance", "100")])
        {
            ModelFunctionNames = [callbackName]
        };
        var ownerObject = new SceneObject
        {
            GaoKey = ownerKey,
            MaterialKey = materialKey,
            Transform = new TransformData { Translation = new(0, 0, 70) }
        };
        var world = new WorldData { Key = worldKey };
        world.Objects.Add(ownerObject);
        var materials = new Dictionary<uint, ResolvedMaterial>
        {
            [materialKey] = new()
            {
                Id = new(materialKey), Type = 5, SourceBytes = CreateMaterialBytes()
            }
        };

        CinematicScriptReachableEngineCall[] engineCalls =
            CinematicScriptReader.ConfiguredModelEngineCalls(document.CinematicScriptOwners[
                new(worldKey, ownerKey)]);
        CinematicScriptReachableEngineCall alphaCall = engineCalls.Single(call =>
            call.Api == "SetLocalAlpha" && call.Line == 2882);
        var runtimeInputs = new Dictionary<string, double?>();
        CinematicScriptLocalAlphaPlayback.AddCurrentObjectPositionInputs(
            document.CinematicScriptOwners[new(worldKey, ownerKey)], ownerObject,
            callbackName, alphaCall.Line, engineCalls, .5f, [], null, runtimeInputs);
        if (runtimeInputs.GetValueOrDefault("a1_z") != 70)
            throw new InvalidDataException("OBJGetPos_C_CURRENT must expose the sampled owner world Z in Jade coordinates.");
        CinematicScriptRliValueFlowAssignment[] assignments =
            CinematicScriptLocalAlphaPlayback.ValueFlowAssignmentsForCall(alphaCall, []);
        if (!CinematicScriptScalarValueFlowEvaluator.TryEvaluateFunction(
                document.CinematicScriptOwners[new(worldKey, ownerKey)], ownerObject,
                callbackName, assignments, engineCalls, .5f, [], _ => null, null, null,
                runtimeInputs, out Dictionary<string, double?> scalarValues)
            || !CinematicScriptScalarValueFlowEvaluator.TryEvaluateSourceExpression(
                "255.0f * v4", scalarValues, out double calculatedAlpha)
            || Math.Abs(calculatedAlpha - 127.5) > .0001)
            throw new InvalidDataException($"The Iris aircraft alpha expression should resolve to 127.5 from its source assignments (v2={scalarValues.GetValueOrDefault("v2")}, v3={scalarValues.GetValueOrDefault("v3")}, v4={scalarValues.GetValueOrDefault("v4")}).");

        CinematicScriptLocalAlphaPlayback playback =
            CinematicScriptLocalAlphaPlayback.Create(document, world, materials, [])
            ?? throw new InvalidDataException("The indexed aircraft SetLocalAlpha callback was not discovered.");
        IReadOnlyDictionary<(uint Material, int Level), byte> alphas = playback.Evaluate(.5f, null);
        if (!alphas.TryGetValue((materialKey, 0), out byte alpha) || alpha != 127)
            throw new InvalidDataException($"OBJGetPos_C_CURRENT and guarded clamp assignments should resolve alpha 127 from the sampled owner pose (actual: {string.Join(",", alphas.Select(entry => $"{entry.Key.Material:X8}/{entry.Key.Level}={entry.Value}"))}; diagnostics: {string.Join(";", playback.Diagnostics)}).");
    }

    private static byte[] CreateMaterialBytes()
    {
        byte[] bytes = new byte[58];
        BitConverter.GetBytes(5).CopyTo(bytes, 0);
        BitConverter.GetBytes((ushort)0xF860).CopyTo(bytes, 38);
        BitConverter.GetBytes(0x00010C00u).CopyTo(bytes, 40);
        return bytes;
    }
}
