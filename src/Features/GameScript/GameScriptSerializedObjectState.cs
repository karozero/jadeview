namespace JadeView;

/// <summary>
/// Projects only the serialized state contributions to initial flags2.
/// Iris gameobject.c:2852-2874 masks the state, maps force flags and restores custom bits.
/// Detection-list allocation and OBJ_UpdateStatusFlagRTL are runtime operations outside
/// this projection; the result does not establish final activity or callback membership.
/// </summary>
internal static class GameScriptSerializedObjectState
{
    internal const uint PreservedStateMask = 0x7B9C0080;
    internal const uint SerializedInvisibleBit = 0x20000000;
    internal const uint SerializedInactiveBit = 0x40000000;
    private const uint CustomBitsMask = 0x0000FF00;

    internal static uint ProjectInitialFlags2(uint serializedState)
    {
        uint flags2 = serializedState & PreservedStateMask;
        if ((serializedState & SerializedInvisibleBit) != 0)
            flags2 |= GameObjectRuntimeFlags.Invisible;
        if ((serializedState & SerializedInactiveBit) != 0)
            flags2 |= GameObjectRuntimeFlags.Inactive;
        return flags2 | (serializedState & CustomBitsMask);
    }
}
