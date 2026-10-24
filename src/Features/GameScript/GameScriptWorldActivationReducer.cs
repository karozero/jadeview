namespace JadeView;

/// <summary>
/// Reduces literal Iris WORPause/WORUnPause requests against an explicit runtime snapshot.
/// It produces state intents only; callers must still model the requested table rebuild.
/// Jaded uses a different, sector-based world path.
/// </summary>
internal static class GameScriptWorldActivationReducer
{
    private const uint PausedStatus = 0x04000000;
    private const uint PauseExclusionStatus = 0x08000000;
    private const uint ActivationBlockedStatus = 0x00400000;
    private const uint ActiveStatus = 0x00000001;

    internal static GameScriptWorldActivationReduction Evaluate(string operation,
        IReadOnlyList<uint> arguments, GameScriptWorldActivationSnapshot? world)
    {
        if (operation is not ("WORPause" or "WORUnPause"))
            return Unknown(GameScriptWorldActivationStatus.UnsupportedOperation,
                "Only source-confirmed Iris WORPause and WORUnPause calls are reduced.");
        if (arguments.Count < 2)
            return Unknown(GameScriptWorldActivationStatus.InvalidArguments,
                "The operation requires a universe world slot and a GAO identity-flags mask.");
        if (world is null)
            return Unknown(GameScriptWorldActivationStatus.WorldSnapshotUnavailable,
                "Runtime object-table state was not supplied.");
        if (world.WorldSlot != arguments[0])
            return Unknown(GameScriptWorldActivationStatus.WorldSlotMismatch,
                $"Command addresses world slot {arguments[0]}, but the supplied snapshot is slot {world.WorldSlot}.");

        uint identityMask = arguments[1];
        GameScriptWorldObjectDelta[] changes = operation == "WORPause"
            ? Pause(world, identityMask)
            : Unpause(world, identityMask);

        return new(GameScriptWorldActivationStatus.Resolved, world.WorldSlot, changes,
            TablesManagerAndEotNeeded: true, VisibilityRefreshNeeded: true, SnPRefreshNeeded: true,
            operation == "WORPause"
                ? "Iris visits the current activated-object table, pauses matching identity flags except objects carrying flags2 0x08000000, then requests table, visibility and SnP refreshes."
                : "Iris visits the full object table, clears flags2 0x04000000 only on identity matches, and independently restores bit 0x1 on every valid object whose 0x00400000 is clear; it then requests table, visibility and SnP refreshes.");
    }

    private static GameScriptWorldObjectDelta[] Pause(GameScriptWorldActivationSnapshot world, uint identityMask) =>
        world.Objects
            .Where(state => state.IsValidObjectPointer
                && state.InActivatedObjectTable
                && (state.Object.Flags & identityMask) != 0
                && (state.RuntimeFlags2 & PauseExclusionStatus) == 0)
            .Select(state => Change(state, state.RuntimeFlags2 | PausedStatus))
            .Where(delta => delta.PreviousFlags2 != delta.ResultFlags2)
            .ToArray();

    private static GameScriptWorldObjectDelta[] Unpause(GameScriptWorldActivationSnapshot world, uint identityMask) =>
        world.Objects
            .Where(state => state.IsValidObjectPointer)
            .Select(state =>
            {
                bool identityMatches = (state.Object.Flags & identityMask) != 0;
                uint flags2 = identityMatches
                    ? state.RuntimeFlags2 & ~PausedStatus
                    : state.RuntimeFlags2;
                if ((flags2 & ActivationBlockedStatus) == 0)
                    flags2 |= ActiveStatus;
                return Change(state, flags2);
            })
            .Where(delta => delta.PreviousFlags2 != delta.ResultFlags2)
            .ToArray();

    private static GameScriptWorldObjectDelta Change(GameScriptWorldObjectSnapshot state, uint flags2) =>
        new(state.Object, state.RuntimeFlags2, flags2);

    private static GameScriptWorldActivationReduction Unknown(GameScriptWorldActivationStatus status,
        string explanation) => new(status, null, [], false, false, false, explanation);
}

internal sealed record GameScriptWorldActivationSnapshot(
    uint WorldSlot,
    IReadOnlyList<GameScriptWorldObjectSnapshot> Objects);

internal sealed record GameScriptWorldObjectSnapshot(
    SceneObject Object,
    uint RuntimeFlags2,
    bool InActivatedObjectTable,
    bool IsValidObjectPointer = true);

internal sealed record GameScriptWorldObjectDelta(
    SceneObject Object,
    uint PreviousFlags2,
    uint ResultFlags2);

internal enum GameScriptWorldActivationStatus
{
    Resolved,
    UnsupportedOperation,
    InvalidArguments,
    WorldSnapshotUnavailable,
    WorldSlotMismatch
}

internal sealed record GameScriptWorldActivationReduction(
    GameScriptWorldActivationStatus Status,
    uint? WorldSlot,
    GameScriptWorldObjectDelta[] ObjectChanges,
    bool TablesManagerAndEotNeeded,
    bool VisibilityRefreshNeeded,
    bool SnPRefreshNeeded,
    string Explanation);
