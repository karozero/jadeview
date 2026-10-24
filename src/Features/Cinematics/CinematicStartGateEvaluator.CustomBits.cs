namespace JadeView;

internal static partial class CinematicStartGateEvaluator
{
    private static CinematicStartGateResult EvaluateCustomBitGate(CinematicScriptOwner owner,
        IEnumerable<SceneObject>? worldObjects,
        (float Time, CinematicLiteralCall Call)[] timelineCalls,
        bool timelineCallsAvailable,
        float playhead,
        (int Mask, string Variable, string Label, string[] MissingInputs) gate)
    {
        int? testedMask = ReadInteger(owner, "des_i_cbit_test1");
        int? expectedState = ReadInteger(owner, "des_i_cbit_etat1");
        CinematicScriptObjectReference? actorReference = owner.EffectiveObjectReference(gate.Variable);
        if (testedMask is null || expectedState is null || actorReference is null)
            return Unknown(gate, "Custom-bit predicate metadata is incomplete.",
                ["des_i_cbit_test1", "des_i_cbit_etat1", gate.Variable + " target"]);

        SceneObject[] matches = worldObjects?
            .Where(sceneObject => sceneObject.GaoKey == actorReference.TargetKey)
            .Take(2)
            .ToArray() ?? [];
        if (matches.Length != 1)
        {
            string targetReason = matches.Length == 0 ? "target is not loaded in this world" : "target is ambiguous";
            return Unknown(gate, $"Custom-bit target {actorReference.TargetKey:X8} {targetReason}.",
                ["unique target object"]);
        }

        SceneObject target = matches[0];
        if (!timelineCallsAvailable)
            return Unknown(gate, "The cinematic call timeline is unavailable, so prior custom-bit writes cannot be ruled out.",
                ["cinematic source and nested-event call history through the playhead"]);
        if (!TryEvaluateCustomBits(target, timelineCalls, playhead, out uint customBits, out string? diagnostic))
            return Unknown(gate, diagnostic!, ["unambiguous source custom-bit history through the playhead"]);

        int matchedBits = unchecked((int)customBits) & testedMask.Value;
        int expectedBits = unchecked(expectedState.Value * testedMask.Value);
        bool passes = matchedBits == expectedBits;
        return new(gate.Mask, gate.Label,
            passes ? CinematicStartGateStatus.Satisfied : CinematicStartGateStatus.Rejected,
            CinematicStartGateEvidenceKind.PreviewObjectState,
            $"{target.Name} ({target.GaoKey:X8}): preview custom bits 0x{customBits:X4}; "
                + $"(0x{customBits:X8} & 0x{testedMask.Value:X8}) = 0x{matchedBits:X8}; "
                + $"expected 0x{expectedBits:X8} from des_i_cbit_etat1 * des_i_cbit_test1.",
            ["gameplay or unlisted scripts may have changed custom bits before the callback"]);
    }

    private static bool TryEvaluateCustomBits(SceneObject target,
        (float Time, CinematicLiteralCall Call)[] timelineCalls,
        float playhead,
        out uint customBits,
        out string? diagnostic)
    {
        const uint customBitMask = 0x0000FF00;
        customBits = target.SerializedStateFlags & customBitMask;
        diagnostic = null;
        if (!float.IsFinite(playhead))
        {
            diagnostic = "The preview playhead is not finite; custom-bit history cannot be evaluated.";
            return false;
        }
        var mutations = timelineCalls
            .Where(item => float.IsFinite(item.Time) && item.Time <= playhead
                && item.Call.Target == target.GaoKey
                && IsCustomBitMutation(item.Call))
            .OrderBy(item => item.Time)
            .ToArray();

        if (mutations.Any(item => !HasCustomBitArguments(item.Call)))
        {
            diagnostic = "A custom-bit setter has an unsupported argument layout; the target state is unresolved.";
            return false;
        }

        foreach (var mutationAtTime in mutations.GroupBy(item => item.Time))
        {
            var callsAtTime = mutationAtTime.ToArray();
            if (callsAtTime.Length > 1)
            {
                diagnostic = $"Multiple custom-bit writes target {target.GaoKey:X8} at {mutationAtTime.Key:F3}s; "
                    + "the current preview data does not establish their execution order.";
                return false;
            }

            customBits = ApplyCustomBitMutation(customBits, callsAtTime[0].Call);
        }

        return true;
    }

    private static bool IsCustomBitMutation(CinematicLiteralCall call) =>
        CinematicEngineFunctionCatalog.NameFor(call.Selector) is "OBJ_CustomBitsSet" or "OBJ_CustomBitsNot";

    private static bool HasCustomBitArguments(CinematicLiteralCall call) =>
        CinematicEngineFunctionCatalog.NameFor(call.Selector) switch
        {
            "OBJ_CustomBitsSet" => call.Arguments.Length == 2,
            "OBJ_CustomBitsNot" => call.Arguments.Length == 1,
            _ => false
        };

    private static uint ApplyCustomBitMutation(uint currentBits, CinematicLiteralCall call)
    {
        const uint customBitMask = 0x0000FF00;
        switch (CinematicEngineFunctionCatalog.NameFor(call.Selector))
        {
            case "OBJ_CustomBitsSet":
                uint addedBits = call.Arguments[0] & customBitMask;
                uint removedBits = call.Arguments[1] & customBitMask;
                return (currentBits & ~removedBits) | addedBits;
            case "OBJ_CustomBitsNot":
                return currentBits ^ (call.Arguments[0] & customBitMask);
            default:
                return currentBits;
        }
    }
}
