using JadeView;

internal static class CinematicScriptCallResultChecks
{
    internal static void Verify()
    {
        var owner = new CinematicScriptOwner(0x100, 0, 0, 0x200,
            0, 0, "owner", [], [], []);
        var ownerObject = new SceneObject
        {
            GaoKey = owner.OwnerKey,
            InitialCapabilities = 0,
            PreviewCapabilities = 0
        };
        var destroyOtherObject = Effect("OBJDestroy", 0x300, "destroyResult", [], 20);
        var guardedEffect = Effect("OBJDestroy", 0x400, null,
            [new("if", "destroyResult == 1000", 21)], 21);

        Dictionary<string, long?> resolved = CinematicScriptCallResultEvaluator.Resolve(owner,
            ownerObject, guardedEffect, [destroyOtherObject, guardedEffect], 0, [], null);
        Check(resolved.TryGetValue("destroyResult", out long? result) && result == 1000,
            "Destroying a uniquely resolved object other than the callback owner returns Iris's source-confirmed 1000 result.");

        CinematicScriptCallbackStateEffect unresolvedPrior = destroyOtherObject with
        {
            ReceiverKey = null,
            BranchConditions = [new("if", "runtime_gate", 19)]
        };
        Dictionary<string, long?> unresolved = CinematicScriptCallResultEvaluator.Resolve(owner,
            ownerObject, guardedEffect, [unresolvedPrior, guardedEffect], 0, [], null);
        Check(unresolved.TryGetValue("destroyResult", out long? unknown) && unknown == null,
            "An unresolved receiver or execution gate must preserve an unknown call result.");

        CinematicScriptCallbackStateEffect skippedPrior = destroyOtherObject with
        {
            BranchConditions = [new("if", "AI_EvalFunc_OBJ_CapaTest_C_CURRENT(0x1)", 19)]
        };
        Dictionary<string, long?> skipped = CinematicScriptCallResultEvaluator.Resolve(owner,
            ownerObject, guardedEffect, [skippedPrior, guardedEffect], 0, [], null);
        Check(!skipped.ContainsKey("destroyResult"),
            "A proven-skipped call must not manufacture a result value.");

        Console.WriteLine("PASS: callback-local engine call results are inferred only for a uniquely resolved non-owner OBJDestroy");
    }

    private static CinematicScriptCallbackStateEffect Effect(string api, uint? receiverKey,
        string? resultVariable, CinematicScriptBranchConditionSource[] branches, int line) => new(
        0x100, "effect", api, "C", [], resultVariable, branches, "callback",
        "source.c", line, "callback", "SourceCallCandidate")
    {
        ReceiverKey = receiverKey
    };

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
