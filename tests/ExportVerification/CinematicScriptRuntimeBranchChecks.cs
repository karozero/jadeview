using JadeView;

internal static class CinematicScriptRuntimeBranchChecks
{
    internal static void Verify()
    {
        var owner = new CinematicScriptOwner(0x10004AE8, 0, 0, 0xFF0045A2,
            0, 0, "callback gate fixture", [], [],
            [new("i_cine", 0, "model", "0"), new("i_cine", 0, "instance", "1")]);
        var branch = new CinematicScriptBranchConditionSource("if",
            "(!v29->i_cine || AI_EvalFunc_OBJ_CapaTest_C_CURRENT(0x100)) && "
            + "(v29->i_cine || AI_EvalFunc_TIMEElapsed_C(v29->f_time, v29->des_f_time0))", 12);

        CinematicScriptBranchEvaluation satisfied = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, 0x100, [branch],
            new Dictionary<string, long?> { ["i_cine"] = 1 });
        Check(satisfied.Value == CinematicScriptBranchValue.True,
            "The evaluator should combine a known capability gate and OVA value using three-valued logic.");

        CinematicScriptBranchEvaluation rejected = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, 0, [branch], new Dictionary<string, long?> { ["i_cine"] = 1 });
        Check(rejected.Value == CinematicScriptBranchValue.False,
            "A known-false conjunct should reject a callback branch even when another predicate is true.");
        CinematicScriptBranchEvaluation assignmentPredicate = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, 0x100,
            [new("if", "(localResult = AI_EvalFunc_OBJ_CapaTest_C_CURRENT(0x100))", 13)],
            new Dictionary<string, long?>());
        Check(assignmentPredicate.Value == CinematicScriptBranchValue.True,
            "A C assignment-expression in a callback predicate should evaluate to its right-hand side value.");

        CinematicScriptBranchEvaluation fractionalPredicate = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, null, [new("if", "des_f_time1 == 0.0", 14)],
            new Dictionary<string, double?> { ["des_f_time1"] = 0.5 });
        CinematicScriptBranchEvaluation fractionalElse = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, null, [new("else", "des_f_time1 == 0.0", 14)],
            new Dictionary<string, double?> { ["des_f_time1"] = 0.5 });
        Check(fractionalPredicate.Value == CinematicScriptBranchValue.False
            && fractionalElse.Value == CinematicScriptBranchValue.True,
            "Callback predicates must preserve fractional OVA values and parse decimal literals.");

        CinematicScriptBranchEvaluation relationalBranches = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, null,
            [new("if", "ratio <= 1.0f && ratio >= 0.0f", 15)],
            new Dictionary<string, double?> { ["ratio"] = 0.5 });
        CinematicScriptBranchEvaluation falseRelationalBranch = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, null, [new("if", "objectZ > waterZ", 16)],
            new Dictionary<string, double?> { ["waterZ"] = 100, ["objectZ"] = 70 });
        CinematicScriptBranchEvaluation unsupportedRelationalBranch = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, null, [new("if", "unknownZ <= waterZ", 17)],
            new Dictionary<string, double?> { ["waterZ"] = 100 });
        CinematicScriptBranchEvaluation bitmaskBranch = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, null, [new("if", "(flags & 0x20) != 0", 18)],
            new Dictionary<string, double?> { ["flags"] = 0x60 });
        CinematicScriptBranchEvaluation arithmeticBranch = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, null, [new("if", "(waterZ - 5.0f - objectZ) / 50.0f >= 0.5f", 19)],
            new Dictionary<string, double?> { ["waterZ"] = 100, ["objectZ"] = 70 });
        CinematicScriptBranchEvaluation precedenceBranch = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, null, [new("if", "2 + 3 * 4 == 14", 20)],
            new Dictionary<string, double?>());
        CinematicScriptBranchEvaluation shiftBranch = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, null, [new("if", "((1 << slot) & 0x8) != 0", 21)],
            new Dictionary<string, double?> { ["slot"] = 3 });
        CinematicScriptBranchEvaluation unsupportedPointerBranch = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, null, [new("if", "(*(uint32_t*)stateMask & 0x8) != 0", 22)],
            new Dictionary<string, double?>());
        Check(relationalBranches.Value == CinematicScriptBranchValue.True
            && falseRelationalBranch.Value == CinematicScriptBranchValue.False
            && unsupportedRelationalBranch.Value == CinematicScriptBranchValue.Unknown
            && bitmaskBranch.Value == CinematicScriptBranchValue.True
            && arithmeticBranch.Value == CinematicScriptBranchValue.True
            && precedenceBranch.Value == CinematicScriptBranchValue.True
            && shiftBranch.Value == CinematicScriptBranchValue.True
            && unsupportedPointerBranch.Value == CinematicScriptBranchValue.Unknown,
            "Callback guards should evaluate bounded arithmetic/bitmasks and preserve unsupported pointer inputs as unknown.");

        string relativeTimerAlias = CinematicScriptCallbackEntryTimeResolver.RelativeClockAliasPrefix
            + "f_time";
        var relativeTimerFields = new Dictionary<string, double?>
        {
            ["f_time"] = 5,
            [relativeTimerAlias] = 1
        };
        CinematicScriptBranchConditionSource elapsedTimerGuard = new("if",
            "AI_EvalFunc_TIMEElapsed_C(v2->f_time, 2.0f)", 23);
        CinematicScriptBranchEvaluation timerBeforeThreshold = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, null, [elapsedTimerGuard], relativeTimerFields, currentTime: 6.99);
        CinematicScriptBranchEvaluation timerAtThreshold = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, null, [elapsedTimerGuard], relativeTimerFields, currentTime: 7.0);
        CinematicScriptBranchEvaluation unprovenTimer = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, null, [elapsedTimerGuard], new Dictionary<string, double?> { ["f_time"] = 5 },
            currentTime: 20);
        Check(timerBeforeThreshold.Value == CinematicScriptBranchValue.False
            && timerAtThreshold.Value == CinematicScriptBranchValue.True
            && unprovenTimer.Value == CinematicScriptBranchValue.Unknown,
            "TIMEElapsed should use only source-proven relative callback timestamps and preserve unknown clock origins.");

        CinematicScriptBranchEvaluation unresolved = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, 0, [branch], new Dictionary<string, long?> { ["i_cine"] = 0 });
        Check(unresolved.Value == CinematicScriptBranchValue.Unknown,
            "Unknown timer/runtime inputs must remain unknown when they affect the branch result.");

        CinematicScriptBranchEvaluation fallback = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, 0, [branch], new Dictionary<string, long?>());
        Check(fallback.Value == CinematicScriptBranchValue.Unknown,
            "An unavailable field value must not be guessed by the branch evaluator.");

        CinematicScriptBranchEvaluation elseBranch = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, 0, [new("else", "v29->i_cine", 20)],
            new Dictionary<string, long?> { ["i_cine"] = 1 });
        Check(elseBranch.Value == CinematicScriptBranchValue.False,
            "An else branch should invert its associated if expression.");

        CinematicScriptBranchEvaluation unknownElse = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, 0, [new("else", "ARRBitTest(2452)", 21)],
            new Dictionary<string, long?> { ["i_cine"] = 1 });
        Check(unknownElse.Value == CinematicScriptBranchValue.Unknown,
            "An else branch with an unsupported saved-state predicate must stay unknown.");

        var savedFlagBranch = new CinematicScriptBranchConditionSource("if",
            "!AI_EvalFunc_ARRBitTest_C((int*)(owner_Flag_speciaux_sauve + 4), 2452)", 23);
        Check(CinematicScriptPredicateEvaluator.SavedFlagIndices([savedFlagBranch]).SequenceEqual([2452]),
            "Only source expressions that explicitly reference the saved-flag buffer should expose assumption controls.");
        CinematicScriptBranchEvaluation clearSavedFlag = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, 0, [savedFlagBranch], new Dictionary<string, long?>(),
            new Dictionary<int, bool?> { [2452] = false });
        Check(clearSavedFlag.Value == CinematicScriptBranchValue.True,
            "A known-clear saved flag should satisfy a negated saved-flag test.");
        CinematicScriptBranchEvaluation setSavedFlag = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, 0, [savedFlagBranch], new Dictionary<string, long?>(),
            new Dictionary<int, bool?> { [2452] = true });
        Check(setSavedFlag.Value == CinematicScriptBranchValue.False,
            "A known-set saved flag should reject a negated saved-flag test.");
        CinematicScriptBranchEvaluation unknownSavedFlag = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, 0, [savedFlagBranch], new Dictionary<string, long?>());
        Check(unknownSavedFlag.Value == CinematicScriptBranchValue.Unknown,
            "Saved flags without an explicit scenario assumption must remain unknown.");
        var nonemptyMessageQueueBranch = new CinematicScriptBranchConditionSource("if",
            "AI_EvalFunc_MSGGetCount_C(AI_gpst_CurrentGameObject)", 25);
        CinematicScriptBranchEvaluation pendingQueueMessage = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, null, [nonemptyMessageQueueBranch],
            new Dictionary<string, long?>
            {
                [CinematicScriptRuntimeBranchEvaluator.CurrentQueueHasMessageField] = 1
            });
        CinematicScriptBranchEvaluation unknownQueueState = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, null, [nonemptyMessageQueueBranch], new Dictionary<string, long?>());
        Check(pendingQueueMessage.Value == CinematicScriptBranchValue.True
            && unknownQueueState.Value == CinematicScriptBranchValue.Unknown,
            "MSGGetCount should be true only while a source-resolved current-object queue message is being reduced.");
        CinematicScriptBranchEvaluation unrelatedBitset = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, 0, [new("if", "ARRBitTest(2452)", 24)], new Dictionary<string, long?>(),
            new Dictionary<int, bool?> { [2452] = false });
        Check(unrelatedBitset.Value == CinematicScriptBranchValue.Unknown,
            "Assumptions must not reinterpret arbitrary bitsets as saved-game flags.");

        CinematicScriptBranchEvaluation effectiveInstanceValue = CinematicScriptRuntimeBranchEvaluator.Evaluate(
            owner, null, [new("if", "i_cine", 22)], 0, []);
        Check(effectiveInstanceValue.Value == CinematicScriptBranchValue.True,
            "Instance OVA values should override model defaults in the evaluator's initial state.");

        var payloadReferenceIsNotRecipient = new CinematicLiteralCall(0x200, 0x1F78,
            [7, owner.OwnerKey, owner.OwnerKey, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        Check(!CinematicScriptRuntimeBranchEvaluator.TryReadQueuedMessageForOwner(
                payloadReferenceIsNotRecipient, owner.OwnerKey, out _),
            "Message GAO payload references must not be confused with the recipient queue.");

        var currentOwnerQueue = new CinematicLiteralCall(owner.OwnerKey, 0x1F78,
            [7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        Check(CinematicScriptRuntimeBranchEvaluator.TryReadQueuedMessageForOwner(
                currentOwnerQueue, owner.OwnerKey, out int queuedMessageId)
            && queuedMessageId == 7,
            "MSGSendAll should resolve its current execution object as the queue recipient.");

        var registeredOwnerQueue = new CinematicLiteralCall(owner.OwnerKey, 0x1F7D,
            [7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        Check(!CinematicScriptRuntimeBranchEvaluator.TryReadQueuedMessageForOwner(
                registeredOwnerQueue, owner.OwnerKey, out _),
            "MSGSendAllOwner must remain unknown until its runtime owner registration is reconstructed.");

        Console.WriteLine("PASS: callback branch preview evaluates supported gates and preserves unknown runtime inputs");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
