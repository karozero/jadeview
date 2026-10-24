namespace JadeView;

internal static class CinematicLightStateTimelineChecks
{
    internal static void VerifyLiteralLightReducer()
    {
        VerifySupportedFieldsAndSeekReset();
        VerifySourceNoOpAndPartialNearFarRules();
        VerifyUnresolvedTargetsRemainDiagnostics();
        VerifyPrefixSnapshotCache();
    }

    private static void VerifySupportedFieldsAndSeekReset()
    {
        const uint lightKey = 0x100;
        const uint relatedKey = 0x200;
        SceneLight baseline = CreateLight(flags: 0, color: 0xFF102030, near: 1, far: 10,
            lightGaoKey: SceneLight.NoLightGaoKey);
        var lightObject = CreateObject(lightKey, "Light", baseline);
        var relatedObject = CreateObject(relatedKey, "Related", null);
        var timeline = new CinematicLightStateTimeline([lightObject, relatedObject],
        [
            (.5f, Call(lightKey, 0x2135, 0xFFABCDEF)),
            (1, Call(lightKey, 0x2136, FloatBits(2), FloatBits(8))),
            (1.5f, Call(lightKey, 0x213B, 3, 1)),
            (2, Call(lightKey, 0x2139, relatedKey))
        ]);
        SceneLight laterSceneEdit = baseline with { Color = 0xFF998877 };
        lightObject.Light = laterSceneEdit;

        var atEnd = timeline.Evaluate(2);
        var final = FindLight(atEnd, lightKey);
        Check(final.Color == 0xFFABCDEF, "Literal LightSetColor should replace the packed color.");
        Check(final.Near == 2 && final.Far == 8, "Literal LightSetNearFar should bit-cast and apply both floats.");
        Check(final.Flags == 8, "LightSetFlag bit index 3 should set the renderer submission bit.");
        Check(final.LightGaoKey == relatedKey, "LightSetGO should store the exact uniquely resolved GAO key.");
        Check(atEnd.Diagnostics.Count(diagnostic => diagnostic.Status == CinematicLightCommandStatus.Applied) == 4,
            "Each supported setter should produce an applied provenance diagnostic.");
        Check(lightObject.Light == laterSceneEdit,
            "Evaluating the preview must not overwrite the scene object or its later source edits.");

        var rewound = timeline.Evaluate(.25f);
        Check(FindLight(rewound, lightKey) == baseline,
            "Seeking before the first event should restore the immutable source baseline.");
        Check(rewound.Diagnostics.Count == 0, "Future commands should not appear in the current diagnostic snapshot.");

        var replayed = timeline.Evaluate(2);
        Check(FindLight(replayed, lightKey) == final,
            "Evaluating the same time after a rewind must produce the same state.");
    }

    private static void VerifySourceNoOpAndPartialNearFarRules()
    {
        const uint typeOneLightKey = 0x300;
        const uint omniLightKey = 0x301;
        SceneLight typeOne = CreateLight(flags: 1, color: 0xFFFFFFFF, near: 3, far: 9);
        SceneLight omni = CreateLight(flags: 0, color: 0xFFFFFFFF, near: 4, far: 12);
        var timeline = new CinematicLightStateTimeline(
            [CreateObject(typeOneLightKey, "Direct", typeOne), CreateObject(omniLightKey, "Omni", omni)],
        [
            (1, Call(typeOneLightKey, 0x2136, FloatBits(1), FloatBits(2))),
            (2, Call(omniLightKey, 0x2136, FloatBits(-1), FloatBits(6)))
        ]);

        var result = timeline.Evaluate(3);
        Check(FindLight(result, typeOneLightKey) == typeOne,
            "Iris LightSetNearFar leaves type-1 lights unchanged.");
        var updatedOmni = FindLight(result, omniLightKey);
        Check(updatedOmni.Near == omni.Near && updatedOmni.Far == 6,
            "Negative near is ignored while an independently nonnegative far value is applied.");
        Check(result.Diagnostics.Any(diagnostic => diagnostic.Status == CinematicLightCommandStatus.UnsupportedLightType),
            "Type-1 no-op should be explicit in diagnostics.");
        Check(result.Diagnostics.Any(diagnostic => diagnostic.Status == CinematicLightCommandStatus.Applied
                && diagnostic.Detail.Contains("retain their previous values", StringComparison.Ordinal)),
            "Partial near/far update should record that the negative input was retained.");
    }

    private static void VerifyUnresolvedTargetsRemainDiagnostics()
    {
        const uint lightKey = 0x400;
        const uint noLightKey = 0x401;
        SceneLight baseline = CreateLight(flags: 0, color: 0x10203040, near: 1, far: 2);
        var lightObject = CreateObject(lightKey, "Known light", baseline);
        var noLightObject = CreateObject(noLightKey, "No light", null);
        var ambiguousA = CreateObject(0x402, "Duplicate A", CreateLight(0, 0, 0, 0));
        var ambiguousB = CreateObject(0x402, "Duplicate B", CreateLight(0, 0, 0, 0));
        var timeline = new CinematicLightStateTimeline(
            [lightObject, noLightObject, ambiguousA, ambiguousB],
        [
            (1, Call(0xDEADBEEF, 0x2135, 0xFFFFFFFF)),
            (2, Call(noLightKey, 0x2135, 0xFFFFFFFF)),
            (3, Call(lightKey, 0x2139, 0xFA17FA17)),
            (4, Call(0x402, 0x2135, 0xFFFFFFFF)),
            (5, Call(lightKey, 0x213B, 40, 1)),
            (6, Call(lightKey, 0x2135))
        ]);

        var result = timeline.Evaluate(6);
        Check(FindLight(result, lightKey) == baseline,
            "Unresolved receiver/reference and malformed commands must not alter baseline state.");
        Check(result.Diagnostics.Any(diagnostic => diagnostic.Status == CinematicLightCommandStatus.UnresolvedReceiver),
            "Unknown and ambiguous receiver keys should be diagnostic, never name-guessed.");
        Check(result.Diagnostics.Any(diagnostic => diagnostic.Status == CinematicLightCommandStatus.ReceiverHasNoLight),
            "A resolved non-light receiver should be reported and left untouched.");
        Check(result.Diagnostics.Any(diagnostic => diagnostic.Status == CinematicLightCommandStatus.UnresolvedRelatedObject),
            "Unresolved LightSetGO references should retain the previous association.");
        Check(result.Diagnostics.Any(diagnostic => diagnostic.Status == CinematicLightCommandStatus.InvalidFlagIndex),
            "Out-of-range light flag indices should be rejected.");
        Check(result.Diagnostics.Any(diagnostic => diagnostic.Status == CinematicLightCommandStatus.InvalidArguments),
            "Wrong argument count should be rejected rather than partially interpreted.");
    }

    private static void VerifyPrefixSnapshotCache()
    {
        const uint lightKey = 0x500;
        var lightObject = CreateObject(lightKey, "Cached light", CreateLight(0, 1, 1, 2));
        var noCommands = new CinematicLightStateTimeline([lightObject], []);
        var baseline = noCommands.Evaluate(0);
        Check(ReferenceEquals(baseline, noCommands.Evaluate(30)),
            "A timeline with no light commands should reuse its baseline snapshot across frames.");
        Check(ReferenceEquals(baseline, noCommands.Evaluate(float.NaN)),
            "A non-finite playhead should map to and reuse the baseline prefix.");

        var timeline = new CinematicLightStateTimeline([lightObject],
        [
            (1, Call(lightKey, 0x2135, 10)),
            (2, Call(lightKey, 0x2135, 20))
        ]);
        var beforeFirst = timeline.Evaluate(.5f);
        var firstPrefix = timeline.Evaluate(1.5f);
        Check(ReferenceEquals(firstPrefix, timeline.Evaluate(1.25f)),
            "Moving backward within the same command prefix should reuse the identical snapshot.");
        Check(!ReferenceEquals(beforeFirst, firstPrefix),
            "Crossing the first command boundary should produce a different snapshot.");
        Check(!ReferenceEquals(firstPrefix, timeline.Evaluate(2)),
            "Crossing another command boundary should produce a different snapshot.");
    }

    private static SceneLight FindLight(CinematicLightStateEvaluation evaluation, uint gaoKey) =>
        evaluation.Lights.Single(state => state.Receiver.GaoKey == gaoKey).Light;

    private static SceneObject CreateObject(uint gaoKey, string name, SceneLight? light) => new()
    {
        GaoKey = gaoKey,
        Name = name,
        Light = light
    };

    private static SceneLight CreateLight(uint flags, uint color, float near, float far,
        uint lightGaoKey = SceneLight.NoLightGaoKey) =>
        new(flags, color, near, far, Density: 0, I8: 0, lightGaoKey);

    private static uint FloatBits(float value) => BitConverter.SingleToUInt32Bits(value);

    private static CinematicLiteralCall Call(uint receiverKey, int selector, params uint[] arguments) =>
        new(receiverKey, selector, arguments);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
