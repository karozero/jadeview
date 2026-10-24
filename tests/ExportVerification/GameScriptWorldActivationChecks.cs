using JadeView;

internal static class GameScriptWorldActivationChecks
{
    internal static void Verify()
    {
        VerifyPauseTouchesOnlyMatchingActivatedObjects();
        VerifyUnpauseClearsMatchingPauseAndReactivatesEveryEligibleObject();
        VerifyWorldRebuildReplacesMembershipUsingTimeMode();
        VerifyUnknownTimeModePreservesOnlyModeIndependentResults();
        VerifyIncompleteRuntimeSnapshotsStayUnknown();
        Console.WriteLine("PASS: Iris WORPause/WORUnPause flags2 intents and world refresh requests");
    }

    private static void VerifyPauseTouchesOnlyMatchingActivatedObjects()
    {
        var matchingActive = Object(0x01, 0x00000001, true);
        var matchingInactive = Object(0x01, 0x00000001, false);
        var matchingExcluded = Object(0x01, 0x08000000, true);
        var otherIdentity = Object(0x02, 0x00000001, true);
        GameScriptWorldActivationSnapshot world = World(3,
            matchingActive, matchingInactive, matchingExcluded, otherIdentity);

        GameScriptWorldActivationReduction result = GameScriptWorldActivationReducer.Evaluate(
            "WORPause", [3, 1], world);

        Require(result.Status == GameScriptWorldActivationStatus.Resolved
            && result.ObjectChanges.Length == 1
            && ReferenceEquals(result.ObjectChanges[0].Object, matchingActive.Object)
            && result.ObjectChanges[0].PreviousFlags2 == 0x00000001
            && result.ObjectChanges[0].ResultFlags2 == 0x04000001
            && RequestsAllWorldRefreshes(result),
            "WORPause must affect only matching members of the activated table, respecting the pause-exclusion bit.");
    }

    private static void VerifyUnpauseClearsMatchingPauseAndReactivatesEveryEligibleObject()
    {
        var matchingActive = Object(0x10, 0x04000000, true);
        var matchingInactive = Object(0x10, 0x04400000, false);
        var otherIdentity = Object(0x20, 0x00000000, false);
        var activationBlocked = Object(0x20, 0x00400000, false);
        var invalidPointer = Object(0x20, 0x00000000, false, isValidPointer: false);
        GameScriptWorldActivationSnapshot world = World(8, matchingActive, matchingInactive,
            otherIdentity, activationBlocked, invalidPointer);

        GameScriptWorldActivationReduction result = GameScriptWorldActivationReducer.Evaluate(
            "WORUnPause", [8, 0x10], world);

        Require(result.Status == GameScriptWorldActivationStatus.Resolved
            && result.ObjectChanges.Length == 3
            && result.ObjectChanges.Single(change => ReferenceEquals(change.Object, matchingActive.Object))
                .ResultFlags2 == 0x00000001
            && result.ObjectChanges.Single(change => ReferenceEquals(change.Object, matchingInactive.Object))
                .ResultFlags2 == 0x00400000
            && result.ObjectChanges.Single(change => ReferenceEquals(change.Object, otherIdentity.Object))
                .ResultFlags2 == 0x00000001
            && result.ObjectChanges.All(change => !ReferenceEquals(change.Object, activationBlocked.Object)
                && !ReferenceEquals(change.Object, invalidPointer.Object))
            && RequestsAllWorldRefreshes(result),
            "WORUnPause must clear pause only on matching objects but restore active bit for every valid, unblocked object.");
    }

    private static void VerifyIncompleteRuntimeSnapshotsStayUnknown()
    {
        GameScriptWorldActivationReduction missingSnapshot = GameScriptWorldActivationReducer.Evaluate(
            "WORPause", [3, 1], null);
        GameScriptWorldActivationReduction wrongSlot = GameScriptWorldActivationReducer.Evaluate(
            "WORPause", [4, 1], World(3, Object(1, 0, true)));
        GameScriptWorldActivationReduction malformed = GameScriptWorldActivationReducer.Evaluate(
            "WORUnPause", [3], World(3));

        Require(missingSnapshot.Status == GameScriptWorldActivationStatus.WorldSnapshotUnavailable
            && wrongSlot.Status == GameScriptWorldActivationStatus.WorldSlotMismatch
            && malformed.Status == GameScriptWorldActivationStatus.InvalidArguments
            && missingSnapshot.ObjectChanges.Length == 0
            && !missingSnapshot.TablesManagerAndEotNeeded,
            "World activation must remain unresolved when the target runtime world or command inputs are unavailable.");
    }

    private static void VerifyWorldRebuildReplacesMembershipUsingTimeMode()
    {
        var normalObject = new SceneObject { Flags = 0 };
        var specialObject = new SceneObject { Flags = 0 };
        var identityExcludedObject = new SceneObject { Flags = 1 };
        GameScriptWorldActivationObjectState[] normalMode = GameScriptWorldActivationRebuildReducer
            .Evaluate(
            [
                new(normalObject, 0x00000000, true),
                new(specialObject, 0x04000000, false),
                new(identityExcludedObject, 0x00000001, true)
            ], specialTime: false).Objects;
        GameScriptWorldActivationObjectState[] specialMode = GameScriptWorldActivationRebuildReducer
            .Evaluate(
            [
                new(normalObject, 0x00000000, true),
                new(specialObject, 0x04000000, false),
                new(identityExcludedObject, 0x00000001, true)
            ], specialTime: true).Objects;

        Require(normalMode[0].ResultFlags2 == 0x00000001
            && normalMode[0].InActivatedObjectTable == true
            && specialMode[0].ResultFlags2 == 0
            && specialMode[0].InActivatedObjectTable == false
            && normalMode[1].ResultFlags2 == 0x04000000
            && specialMode[1].ResultFlags2 == 0x04000001
            && specialMode[1].InActivatedObjectTable == true
            && normalMode[2].ResultFlags2 == 0
            && normalMode[2].InActivatedObjectTable == false,
            "World rebuild must clear previous membership and apply the distinct normal/special-time activation predicate plus the GAO identity exclusion.");
    }

    private static void VerifyUnknownTimeModePreservesOnlyModeIndependentResults()
    {
        var timeDependent = new SceneObject { Flags = 0 };
        var blockedInBothModes = new SceneObject { Flags = 0 };
        var identityExcluded = new SceneObject { Flags = 1 };
        var invalidPointer = new SceneObject { Flags = 0 };

        GameScriptWorldActivationRebuild result = GameScriptWorldActivationRebuildReducer.Evaluate(
        [
            new(timeDependent, 0x00000000, false),
            new(blockedInBothModes, 0x00400000, true),
            new(identityExcluded, 0x00000001, true),
            new(invalidPointer, 0x00000000, true, IsValidObjectPointer: false)
        ], specialTime: null);

        Require(result.Status == GameScriptWorldActivationRebuildStatus.PartiallyUnknown
            && result.Objects.Length == 3
            && result.Objects.Single(state => ReferenceEquals(state.Object, timeDependent))
                .InActivatedObjectTable is null
            && result.Objects.Single(state => ReferenceEquals(state.Object, blockedInBothModes))
                .InActivatedObjectTable == false
            && result.Objects.Single(state => ReferenceEquals(state.Object, identityExcluded))
                .InActivatedObjectTable == false,
            "An unknown ENG_gf_SpecialTime must preserve deterministic outcomes while leaving mode-dependent activation unresolved.");
    }

    private static GameScriptWorldObjectSnapshot Object(uint identityFlags, uint runtimeFlags2,
        bool inActivatedTable, bool isValidPointer = true) =>
        new(new SceneObject { Flags = identityFlags }, runtimeFlags2, inActivatedTable, isValidPointer);

    private static GameScriptWorldActivationSnapshot World(uint slot,
        params GameScriptWorldObjectSnapshot[] objects) => new(slot, objects);

    private static bool RequestsAllWorldRefreshes(GameScriptWorldActivationReduction result) =>
        result.TablesManagerAndEotNeeded && result.VisibilityRefreshNeeded && result.SnPRefreshNeeded;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
