using JadeView;

internal static class CinematicScriptCallbackWindowChecks
{
    internal static void Verify()
    {
        const uint ownerKey = 0x12345678;
        var owner = new CinematicScriptOwner(ownerKey, 0, 0, 0xFF000001,
            0, 0, "callback window fixture", [], [], []);
        var ownerObject = new SceneObject { GaoKey = ownerKey, InitialCapabilities = 0 };
        var entry = new CinematicScriptFunctionPath("opening", "opening", []);
        var exit = new CinematicScriptFunctionPath("wait", "opening -> wait",
        [
            new CinematicScriptCallbackPathStep("opening", "wait", "replace-current-track", 0,
                null, null, null, null,
                [new("if", "AI_EvalFunc_OBJ_CapaTest_C_CURRENT(0x1)", 20)], true)
        ]);
        var capabilitySet = new CinematicLiteralCall(ownerKey, 0x1FC5, [1, 0]);
        var commands = new[] { (5f, capabilitySet) };

        CinematicScriptCallbackWindow? bounded = CinematicScriptCallbackWindowResolver.Resolve(
            owner, ownerObject, "opening", entry.Steps, [],
            new Dictionary<string, long?>(), [exit], commands, null);
        Check(bounded is { Start: 0, End: 5 },
            "A callback interval should end at the first source-resolved capability transition.");

        var unknownTimerExit = exit with
        {
            Steps =
            [
                exit.Steps[0] with
                {
                    BranchConditions =
                    [new("if", "AI_EvalFunc_TIMEElapsed_C(owner->f_start, owner->f_duration)", 21)]
                }
            ]
        };
        CinematicScriptCallbackWindow? unbounded = CinematicScriptCallbackWindowResolver.Resolve(
            owner, ownerObject, "opening", entry.Steps, [],
            new Dictionary<string, long?>(), [unknownTimerExit], commands, null);
        Check(unbounded == null,
            "A callback with an unresolved timer exit must not be stretched across later cinematic time.");

        CinematicScriptCallbackWindow? noExit = CinematicScriptCallbackWindowResolver.Resolve(
            owner, ownerObject, "opening", entry.Steps, [],
            new Dictionary<string, long?>(), [], commands, null);
        Check(noExit == null,
            "A callback without a recovered exit must not be treated as a clip-long event.");

        CinematicScriptCallbackWindow? terminalWithoutExit = CinematicScriptCallbackWindowResolver.Resolve(
            owner, ownerObject, "opening", entry.Steps, [],
            new Dictionary<string, long?>(), [], commands, null,
            allowUnboundedStateMutation: true);
        Check(terminalWithoutExit is { Start: 0, End: null },
            "A source-confirmed persistent state mutation may remain active after a resolved entry without an exit transition.");

        var sameTrackContinuation = exit with
        {
            Path = "opening -> opening",
            Steps = [exit.Steps[0] with { ToFunction = "opening" }]
        };
        CinematicScriptCallbackWindow? continuedTrack = CinematicScriptCallbackWindowResolver.Resolve(
            owner, ownerObject, "opening", entry.Steps, [],
            new Dictionary<string, long?>(), [sameTrackContinuation], commands, null,
            allowUnboundedStateMutation: true);
        Check(continuedTrack is { Start: 0, End: null },
            "AITrackCurChangeNow targeting the active function must continue the callback instead of closing its window.");

        var unresolvedEntry = entry with
        {
            Steps =
            [
                new CinematicScriptCallbackPathStep("root", "opening", "replace-current-track", 0,
                    null, null, null, null,
                    [new("if", "AI_EvalFunc_TIMEElapsed_C(owner->f_start, owner->f_duration)", 19)], true)
            ]
        };
        CinematicScriptCallbackWindow? unresolvedTerminal = CinematicScriptCallbackWindowResolver.Resolve(
            owner, ownerObject, "opening", unresolvedEntry.Steps, [],
            new Dictionary<string, long?>(), [], commands, null,
            allowUnboundedStateMutation: true);
        Check(unresolvedTerminal == null,
            "A persistent state mutation must not bypass an unresolved callback-entry route.");

        var unreachableEntry = entry with
        {
            Steps =
            [
                new CinematicScriptCallbackPathStep("root", "opening", "replace-current-track", 0,
                    null, null, null, null,
                    [new("if", "AI_EvalFunc_OBJ_CapaTest_C_CURRENT(0x2)", 19)], true)
            ]
        };
        ownerObject.InitialCapabilities = 0;
        Check(CinematicScriptCallbackWindowResolver.IsPathProvenUnreachable(owner, ownerObject,
                unreachableEntry, [], null),
            "A callback path that evaluates false at every sampled state should be skippable in branch consensus.");
        Check(!CinematicScriptCallbackWindowResolver.IsPathProvenUnreachable(owner, ownerObject,
                unknownTimerExit with { FunctionName = "opening" }, [], null),
            "An unresolved callback gate must not be classified as unreachable.");

        Console.WriteLine("PASS: callback preview distinguishes bounded, persistent, proven-unreachable, and unresolved routes");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
