namespace JadeView;

internal static class GameScriptEotMembershipResolver
{
    private const GameScriptEotTable KnownTables =
        GameScriptEotTable.AnimatedObjects
        | GameScriptEotTable.DynamicObjects
        | GameScriptEotTable.AiCallbacks
        | GameScriptEotTable.CollisionMap
        | GameScriptEotTable.CollisionZdm
        | GameScriptEotTable.CollisionZde
        | GameScriptEotTable.EventObjects
        | GameScriptEotTable.HierarchyObjects;

    internal static GameScriptEotMembership Evaluate(uint identityFlags) =>
        new(identityFlags, (GameScriptEotTable)(identityFlags & (uint)KnownTables));
}
