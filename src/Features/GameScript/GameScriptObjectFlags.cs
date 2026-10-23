namespace JadeView;

/// <summary>
/// Decodes the flags2 mutation of a resolved script receiver. Dispatch, group
/// membership, world-table refresh, sound and hierarchy side effects are separate.
/// Clearing ForceInactive is not proof that the world scheduler activates the object.
/// </summary>
internal static class GameScriptObjectFlags
{
    private const int ObjectFlagsSet = 0x1FB3;
    private const int ObjectInvisibleSet = 0x1FD8, ObjectInactiveSet = 0x1FD9;
    private const int GroupInvisibleSet = 0x3BC4, GroupInactiveSet = 0x3BC5;

    internal static GameScriptObjectFlagChange? Decode(uint targetKey, int selector,
        ReadOnlySpan<uint> arguments)
    {
        if (selector == ObjectFlagsSet && arguments.Length == 2)
            return new(targetKey, arguments[0] & GameObjectRuntimeFlags.ControlMask,
                arguments[1] & GameObjectRuntimeFlags.ControlMask, false);

        bool targetsGroup = selector is GroupInvisibleSet or GroupInactiveSet;
        bool supported = targetsGroup || selector is ObjectInvisibleSet or ObjectInactiveSet;
        if (!supported || arguments.Length != 1) return null;

        uint mask = selector is ObjectInvisibleSet or GroupInvisibleSet
            ? GameObjectRuntimeFlags.Invisible : GameObjectRuntimeFlags.Inactive;
        return arguments[0] != 0
            ? new(targetKey, mask, 0, targetsGroup)
            : new(targetKey, 0, mask, targetsGroup);
    }
}
