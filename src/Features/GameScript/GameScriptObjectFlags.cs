namespace JadeView;

/// <summary>
/// Decodes the flags2 mutation of a resolved script receiver. Dispatch, group
/// membership, world-table refresh, sound and hierarchy side effects are separate.
/// Clearing ForceInactive is not proof that the world scheduler activates the object.
/// </summary>
internal static class GameScriptObjectFlags
{
    internal static GameScriptObjectFlagChange? Decode(uint targetKey, int selector,
        ReadOnlySpan<uint> arguments)
    {
        string function = CinematicEngineFunctionCatalog.NameFor(selector);
        if (function == "OBJ_FlagsControlSet" && arguments.Length == 2)
            return new(targetKey, arguments[0] & GameObjectRuntimeFlags.ControlMask,
                arguments[1] & GameObjectRuntimeFlags.ControlMask, false);

        bool targetsGroup = function is "GRPInvisibleSet" or "GRPInactiveSet";
        bool supported = targetsGroup || function is "OBJ_FlagInvisibleSet" or "OBJ_FlagInactiveSet";
        if (!supported || arguments.Length != 1) return null;

        uint mask = function is "OBJ_FlagInvisibleSet" or "GRPInvisibleSet"
            ? GameObjectRuntimeFlags.Invisible : GameObjectRuntimeFlags.Inactive;
        return arguments[0] != 0
            ? new(targetKey, mask, 0, targetsGroup)
            : new(targetKey, 0, mask, targetsGroup);
    }
}
