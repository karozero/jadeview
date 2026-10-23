using JadeView;

internal static class GameWorldUniverseReducerChecks
{
    private const uint HangarWorld = 0xFF005833;
    private const uint CellarWorld = 0xFF00218E;

    internal static void Verify()
    {
        VerifyAddUsesResolvedWorldAndDoesNotActivateIt();
        VerifyExistingWorldAddReturnsItsOriginalSlot();
        VerifyActiveAndDesactiveTrackUniverseFlagsSeparately();
        VerifyAddDoesNotGuessTableHoleCompaction();
        VerifyUnknownWorldMutationsInvalidateLaterSlotInterpretation();
        VerifyDestroyDoesNotLeaveStaleSlotsUsable();
        VerifyMissingInputsRemainUnresolved();
        Console.WriteLine("PASS: general game-script world-universe reducer preserves source slot semantics");
    }

    private static void VerifyDestroyDoesNotLeaveStaleSlotsUsable()
    {
        GameWorldUniverseState knownOrder = GameWorldUniverseState.FromKnownSlots(
            [new(HangarWorld, false)]);
        GameWorldUniverseEvaluation result = GameWorldUniverseReducer.Evaluate(knownOrder,
        [
            Command(GameWorldUniverseReducer.DestroyWorldSelector, 0, 0),
            Command(GameWorldUniverseReducer.ActivateSelector, 0)
        ], new HashSet<uint>());

        Require(!result.State.SlotOrderKnown
            && result.Operations[0].Status == GameWorldUniverseOperationStatus.Unsupported
            && result.Operations[1].Status == GameWorldUniverseOperationStatus.Unresolved
            && result.Diagnostics.Any(item =>
                item.Code == GameWorldUniverseDiagnosticCode.UnsupportedWorldDestroy)
            && result.Diagnostics.Any(item =>
                item.Code == GameWorldUniverseDiagnosticCode.InitialSlotOrderUnknown),
            "WORUniverseDestroy must invalidate the slot projection before later slot-indexed operations.");
    }

    private static void VerifyUnknownWorldMutationsInvalidateLaterSlotInterpretation()
    {
        GameWorldUniverseState knownOrder = GameWorldUniverseState.FromKnownSlots(
            [new(HangarWorld, false)]);
        GameWorldUniverseEvaluation afterWorldList = GameWorldUniverseReducer.Evaluate(knownOrder,
        [
            Command(GameWorldUniverseReducer.LoadListSelector, CellarWorld),
            Command(GameWorldUniverseReducer.ActivateSelector, 0)
        ], new HashSet<uint> { CellarWorld });
        GameWorldUniverseEvaluation afterUnresolvedAdd = GameWorldUniverseReducer.Evaluate(knownOrder,
        [
            Command(GameWorldUniverseReducer.UniverseAddSelector, 0xFF0045A2),
            Command(GameWorldUniverseReducer.UniverseAddSelector, CellarWorld)
        ], new HashSet<uint> { CellarWorld });

        Require(!afterWorldList.State.SlotOrderKnown
            && afterWorldList.Operations[1].Status == GameWorldUniverseOperationStatus.Unresolved
            && afterWorldList.Diagnostics.Any(item =>
                item.Code == GameWorldUniverseDiagnosticCode.InitialSlotOrderUnknown)
            && !afterUnresolvedAdd.State.SlotOrderKnown
            && afterUnresolvedAdd.Operations[1].Status == GameWorldUniverseOperationStatus.Unresolved
            && afterUnresolvedAdd.Diagnostics.Any(item =>
                item.Code == GameWorldUniverseDiagnosticCode.InitialSlotOrderUnknown),
            "An unsupported world-list replacement or unresolved AddWorld must invalidate prior slot assumptions before later slot-indexed calls.");
    }

    private static void VerifyAddDoesNotGuessTableHoleCompaction()
    {
        GameWorldUniverseState initial = GameWorldUniverseState.FromKnownSlots(
            [new(HangarWorld, false), new(null, null), new(CellarWorld, true)]);
        GameWorldUniverseEvaluation result = GameWorldUniverseReducer.Evaluate(initial,
            [Command(GameWorldUniverseReducer.UniverseAddSelector, 0xFF0045A2)],
            new HashSet<uint> { 0xFF0045A2 });

        Require(result.Diagnostics.Single().Code == GameWorldUniverseDiagnosticCode.TableCompactionUnknown
            && !result.State.SlotOrderKnown
            && result.State.Slots.Count == 0
            && result.Operations.Single().Status == GameWorldUniverseOperationStatus.Unresolved,
            "When the source table has holes, AddWorld must invalidate slot assumptions instead of guessing whether capacity exhaustion compacts and shifts them.");
    }

    private static void VerifyAddUsesResolvedWorldAndDoesNotActivateIt()
    {
        GameWorldUniverseState initial = GameWorldUniverseState.FromKnownSlots(
            [new(HangarWorld, false)]);
        GameWorldUniverseEvaluation result = GameWorldUniverseReducer.Evaluate(initial,
            [Command(GameWorldUniverseReducer.UniverseAddSelector, CellarWorld)],
            new HashSet<uint> { CellarWorld });

        Require(result.State.Slots.SequenceEqual(
                [new GameWorldUniverseSlot(HangarWorld, false), new GameWorldUniverseSlot(CellarWorld, false)])
            && result.Operations.Single().Status == GameWorldUniverseOperationStatus.Added
            && result.Operations.Single().WorldSlot == 1
            && !result.State.CurrentWorldKnown && result.State.CurrentWorldKey is null,
            "WORUniverseAdd should append a resolvable world in order without selecting it as the current or universe-active world.");
    }

    private static void VerifyExistingWorldAddReturnsItsOriginalSlot()
    {
        GameWorldUniverseState initial = GameWorldUniverseState.FromKnownSlots(
            [new(HangarWorld, false), new(CellarWorld, true)], currentWorldKey: HangarWorld);
        GameWorldUniverseEvaluation result = GameWorldUniverseReducer.Evaluate(initial,
            [Command(GameWorldUniverseReducer.UniverseAddSelector, CellarWorld)],
            new HashSet<uint>());

        Require(result.State.Slots.Count == 2
            && result.Operations.Single().Status == GameWorldUniverseOperationStatus.AlreadyPresent
            && result.Operations.Single().WorldSlot == 1
            && result.State.CurrentWorldKey == HangarWorld,
            "Adding an already loaded world should return its existing slot without duplicating or changing activation.");
    }

    private static void VerifyActiveAndDesactiveTrackUniverseFlagsSeparately()
    {
        GameWorldUniverseState initial = GameWorldUniverseState.FromKnownSlots(
            [new(HangarWorld, false), new(CellarWorld, false)]);
        GameWorldUniverseEvaluation result = GameWorldUniverseReducer.Evaluate(initial,
        [
            Command(GameWorldUniverseReducer.ActivateSelector, 1),
            Command(GameWorldUniverseReducer.DeactivateSelector, 1)
        ], new HashSet<uint>());

        Require(result.State.Slots[1].IsUniverseActive == false
            && result.State.CurrentWorldKnown
            && result.State.CurrentWorldKey == CellarWorld
            && result.Operations.Select(operation => operation.Status).SequenceEqual(
                [GameWorldUniverseOperationStatus.Activated, GameWorldUniverseOperationStatus.Deactivated]),
            "WORActive selects the addressed current world; WORDesactive clears the active flag without clearing the current-world pointer.");
    }

    private static void VerifyMissingInputsRemainUnresolved()
    {
        GameWorldUniverseEvaluation unknownOrder = GameWorldUniverseReducer.Evaluate(
            GameWorldUniverseState.Unknown(),
            [Command(GameWorldUniverseReducer.ActivateSelector, 0)], new HashSet<uint>());
        GameWorldUniverseState knownOrder = GameWorldUniverseState.FromKnownSlots([]);
        GameWorldUniverseEvaluation missingWorld = GameWorldUniverseReducer.Evaluate(knownOrder,
            [Command(GameWorldUniverseReducer.UniverseAddSelector, CellarWorld)], new HashSet<uint>());
        GameWorldUniverseEvaluation worldList = GameWorldUniverseReducer.Evaluate(knownOrder,
            [Command(GameWorldUniverseReducer.LoadListSelector, HangarWorld)], new HashSet<uint> { HangarWorld });

        Require(unknownOrder.Diagnostics.Single().Code == GameWorldUniverseDiagnosticCode.InitialSlotOrderUnknown
            && unknownOrder.State.Slots.Count == 0
            && missingWorld.Diagnostics.Single().Code == GameWorldUniverseDiagnosticCode.WorldKeyUnresolved
            && missingWorld.State.Slots.Count == 0
            && worldList.Diagnostics.Single().Code == GameWorldUniverseDiagnosticCode.UnsupportedWorldList
            && !worldList.State.SlotOrderKnown
            && worldList.State.Slots.Count == 0,
            "Unknown initial slots, unresolved disk keys, and WORLoadList must remain explicit barriers without invented world state.");
    }

    private static GameScriptLiteralCommand Command(int selector, params uint[] arguments) =>
        new(selector, Array.AsReadOnly(arguments));

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
