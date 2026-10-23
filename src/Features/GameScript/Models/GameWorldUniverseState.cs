namespace JadeView;

internal readonly record struct GameWorldUniverseSlot(uint? WorldKey, bool? IsUniverseActive)
{
    internal bool IsHole => WorldKey is null;
}

internal sealed class GameWorldUniverseState
{
    private readonly GameWorldUniverseSlot[] slots;

    private GameWorldUniverseState(bool slotOrderKnown, GameWorldUniverseSlot[] slots,
        bool currentWorldKnown, uint? currentWorldKey)
    {
        SlotOrderKnown = slotOrderKnown;
        this.slots = slots;
        Slots = Array.AsReadOnly(this.slots);
        CurrentWorldKnown = currentWorldKnown;
        CurrentWorldKey = currentWorldKey;
    }

    internal bool SlotOrderKnown { get; }
    internal IReadOnlyList<GameWorldUniverseSlot> Slots { get; }
    internal bool CurrentWorldKnown { get; }
    internal uint? CurrentWorldKey { get; }

    internal static GameWorldUniverseState Unknown() => new(false, [], false, null);

    internal static GameWorldUniverseState FromKnownSlots(IEnumerable<GameWorldUniverseSlot> slots,
        uint? currentWorldKey = null) => new(true, slots.ToArray(), currentWorldKey.HasValue, currentWorldKey);

    internal static GameWorldUniverseState Create(bool slotOrderKnown,
        IEnumerable<GameWorldUniverseSlot>? slots = null, bool currentWorldKnown = false,
        uint? currentWorldKey = null) => new(slotOrderKnown, slots?.ToArray() ?? [],
            currentWorldKnown, currentWorldKey);

    internal GameWorldUniverseState WithSlots(GameWorldUniverseSlot[] updatedSlots,
        bool currentWorldKnown, uint? currentWorldKey) => new(SlotOrderKnown, updatedSlots,
            currentWorldKnown, currentWorldKey);
}

internal sealed record GameWorldUniverseEvaluation(GameWorldUniverseState State,
    IReadOnlyList<GameWorldUniverseOperationResult> Operations,
    IReadOnlyList<GameWorldUniverseDiagnostic> Diagnostics);

internal sealed record GameWorldUniverseOperationResult(int CommandIndex, int Selector,
    GameWorldUniverseOperationStatus Status, uint? WorldKey, int? WorldSlot);

internal sealed record GameWorldUniverseDiagnostic(int CommandIndex,
    GameWorldUniverseDiagnosticCode Code, string Message);

internal enum GameWorldUniverseOperationStatus
{
    Added,
    AlreadyPresent,
    Activated,
    Deactivated,
    Unresolved,
    Unsupported
}

internal enum GameWorldUniverseDiagnosticCode
{
    InvalidArgumentCount,
    WorldKeyUnresolved,
    InitialSlotOrderUnknown,
    InvalidWorldSlot,
    TableCompactionUnknown,
    UnsupportedWorldList,
    UnsupportedWorldDestroy,
    UnsupportedSelector
}
