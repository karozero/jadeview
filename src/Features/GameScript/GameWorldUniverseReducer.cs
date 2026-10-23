namespace JadeView;

internal static class GameWorldUniverseReducer
{
    internal const int UniverseAddSelector = 0x1F40;
    internal const int ActivateSelector = 0x1F41;
    internal const int DeactivateSelector = 0x1F42;
    internal const int DestroyWorldSelector = 0x1F45;
    internal const int LoadListSelector = 0x1F46;

    internal static GameWorldUniverseEvaluation Evaluate(GameWorldUniverseState initialState,
        IEnumerable<GameScriptLiteralCommand> commands, IReadOnlySet<uint> resolvedWorldKeys)
    {
        GameWorldUniverseState state = initialState;
        List<GameWorldUniverseOperationResult> operations = [];
        List<GameWorldUniverseDiagnostic> diagnostics = [];

        foreach ((GameScriptLiteralCommand command, int commandIndex) in commands.Select(
                     (command, commandIndex) => (command, commandIndex)))
        {
            GameWorldUniverseOperationResult result = Apply(command, commandIndex, state,
                resolvedWorldKeys, diagnostics, out GameWorldUniverseState updatedState);
            operations.Add(result);
            state = updatedState;
        }

        return new(state, Array.AsReadOnly(operations.ToArray()),
            Array.AsReadOnly(diagnostics.ToArray()));
    }

    private static GameWorldUniverseOperationResult Apply(GameScriptLiteralCommand command,
        int commandIndex, GameWorldUniverseState state, IReadOnlySet<uint> resolvedWorldKeys,
        List<GameWorldUniverseDiagnostic> diagnostics, out GameWorldUniverseState updatedState)
    {
        updatedState = state;
        if (command.Selector == LoadListSelector)
        {
            diagnostics.Add(new(commandIndex, GameWorldUniverseDiagnosticCode.UnsupportedWorldList,
                "WORLoadList requires parsing and ordering a world-list resource; this reducer does not load it."));
            updatedState = GameWorldUniverseState.Unknown();
            return Result(commandIndex, command, GameWorldUniverseOperationStatus.Unsupported);
        }

        if (command.Selector == DestroyWorldSelector)
        {
            diagnostics.Add(new(commandIndex, GameWorldUniverseDiagnosticCode.UnsupportedWorldDestroy,
                "WORUniverseDestroy can remove a world slot and alter current-world state; deletion is not reduced here."));
            updatedState = GameWorldUniverseState.Unknown();
            return Result(commandIndex, command, GameWorldUniverseOperationStatus.Unsupported);
        }

        if (command.Selector is not (UniverseAddSelector or ActivateSelector or DeactivateSelector))
        {
            diagnostics.Add(new(commandIndex, GameWorldUniverseDiagnosticCode.UnsupportedSelector,
                $"World-universe reducer does not handle selector 0x{command.Selector:X4}."));
            return Result(commandIndex, command, GameWorldUniverseOperationStatus.Unsupported);
        }

        if (command.Arguments.Count != 1)
        {
            diagnostics.Add(new(commandIndex, GameWorldUniverseDiagnosticCode.InvalidArgumentCount,
                $"Selector 0x{command.Selector:X4} expects one literal integer argument."));
            updatedState = GameWorldUniverseState.Unknown();
            return Result(commandIndex, command, GameWorldUniverseOperationStatus.Unresolved);
        }

        uint value = command.Arguments[0];
        if (command.Selector == UniverseAddSelector)
            return AddWorld(commandIndex, command, value, state, resolvedWorldKeys, diagnostics,
                out updatedState);

        return SetActive(commandIndex, command, value, state, diagnostics, out updatedState);
    }

    private static GameWorldUniverseOperationResult AddWorld(int commandIndex,
        GameScriptLiteralCommand command, uint worldKey, GameWorldUniverseState state,
        IReadOnlySet<uint> resolvedWorldKeys, List<GameWorldUniverseDiagnostic> diagnostics,
        out GameWorldUniverseState updatedState)
    {
        updatedState = state;
        if (!state.SlotOrderKnown)
        {
            diagnostics.Add(new(commandIndex, GameWorldUniverseDiagnosticCode.InitialSlotOrderUnknown,
                "WORUniverseAdd may load the world, but its universe slot cannot be projected because the initial slot order is unknown."));
            return Result(commandIndex, command, GameWorldUniverseOperationStatus.Unresolved, worldKey);
        }

        int existingSlot = FindWorldSlot(state.Slots, worldKey);
        if (existingSlot >= 0)
            return Result(commandIndex, command, GameWorldUniverseOperationStatus.AlreadyPresent,
                worldKey, existingSlot);

        if (!resolvedWorldKeys.Contains(worldKey))
        {
            diagnostics.Add(new(commandIndex, GameWorldUniverseDiagnosticCode.WorldKeyUnresolved,
                $"WORUniverseAdd references world key 0x{worldKey:X8}, which is not in the supplied resolved-world set."));
            updatedState = GameWorldUniverseState.Unknown();
            return Result(commandIndex, command, GameWorldUniverseOperationStatus.Unresolved, worldKey);
        }

        if (state.Slots.Any(slot => slot.IsHole))
        {
            diagnostics.Add(new(commandIndex, GameWorldUniverseDiagnosticCode.TableCompactionUnknown,
                "The source table compacts holes when it is full; capacity is not part of this snapshot, so the new slot index cannot be predicted."));
            updatedState = GameWorldUniverseState.Unknown();
            return Result(commandIndex, command, GameWorldUniverseOperationStatus.Unresolved, worldKey);
        }

        GameWorldUniverseSlot[] expandedSlots = [.. state.Slots, new GameWorldUniverseSlot(worldKey, false)];
        updatedState = state.WithSlots(expandedSlots, state.CurrentWorldKnown, state.CurrentWorldKey);
        return Result(commandIndex, command, GameWorldUniverseOperationStatus.Added,
            worldKey, expandedSlots.Length - 1);
    }

    private static GameWorldUniverseOperationResult SetActive(int commandIndex,
        GameScriptLiteralCommand command, uint worldSlot, GameWorldUniverseState state,
        List<GameWorldUniverseDiagnostic> diagnostics, out GameWorldUniverseState updatedState)
    {
        updatedState = state;
        if (!state.SlotOrderKnown)
        {
            diagnostics.Add(new(commandIndex, GameWorldUniverseDiagnosticCode.InitialSlotOrderUnknown,
                $"WOR{(command.Selector == ActivateSelector ? "Active" : "Desactive")} uses universe slot {worldSlot}, but the initial slot order is unknown."));
            return Result(commandIndex, command, GameWorldUniverseOperationStatus.Unresolved);
        }

        if (worldSlot >= state.Slots.Count)
        {
            diagnostics.Add(new(commandIndex, GameWorldUniverseDiagnosticCode.InvalidWorldSlot,
                $"Universe slot {worldSlot} is outside the known {state.Slots.Count}-slot table."));
            return Result(commandIndex, command, GameWorldUniverseOperationStatus.Unresolved);
        }

        int slotIndex = (int)worldSlot;
        GameWorldUniverseSlot[] updatedSlots = state.Slots.ToArray();
        GameWorldUniverseSlot selectedSlot = updatedSlots[slotIndex];
        if (selectedSlot.IsHole)
        {
            diagnostics.Add(new(commandIndex, GameWorldUniverseDiagnosticCode.InvalidWorldSlot,
                $"Universe slot {worldSlot} is a table hole, not a loaded world."));
            return Result(commandIndex, command, GameWorldUniverseOperationStatus.Unresolved);
        }
        bool activate = command.Selector == ActivateSelector;
        updatedSlots[slotIndex] = selectedSlot with { IsUniverseActive = activate };

        // Source WORActive sets CurrentWorld from this slot. WORDesactive only clears
        // the slot flag and leaves the current-world pointer untouched.
        bool currentWorldKnown = activate || state.CurrentWorldKnown;
        uint? currentWorldKey = activate ? selectedSlot.WorldKey : state.CurrentWorldKey;
        updatedState = state.WithSlots(updatedSlots, currentWorldKnown, currentWorldKey);

        return Result(commandIndex, command,
            activate ? GameWorldUniverseOperationStatus.Activated : GameWorldUniverseOperationStatus.Deactivated,
            selectedSlot.WorldKey, slotIndex);
    }

    private static int FindWorldSlot(IReadOnlyList<GameWorldUniverseSlot> slots, uint worldKey)
    {
        for (int slotIndex = 0; slotIndex < slots.Count; slotIndex++)
        {
            if (slots[slotIndex].WorldKey == worldKey)
                return slotIndex;
        }

        return -1;
    }

    private static GameWorldUniverseOperationResult Result(int commandIndex,
        GameScriptLiteralCommand command, GameWorldUniverseOperationStatus status,
        uint? worldKey = null, int? worldSlot = null) =>
        new(commandIndex, command.Selector, status, worldKey, worldSlot);
}
