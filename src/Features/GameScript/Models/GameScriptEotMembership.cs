namespace JadeView;

/// <summary>
/// Source-backed Iris EOT table membership derived from GAO identity flags.
/// Membership is conditional on the object entering the activated-object table.
/// </summary>
[Flags]
internal enum GameScriptEotTable : uint
{
    None = 0,
    AnimatedObjects = 0x00000002,
    DynamicObjects = 0x00000004,
    AiCallbacks = 0x00000020,
    CollisionMap = 0x00000100,
    CollisionZdm = 0x00000200,
    CollisionZde = 0x00000400,
    EventObjects = 0x02000000,
    HierarchyObjects = 0x00400000
}

internal sealed record GameScriptEotMembership(uint IdentityFlags, GameScriptEotTable Tables)
{
    internal string[] TableNames => Enum.GetValues<GameScriptEotTable>()
        .Where(table => table != GameScriptEotTable.None && Tables.HasFlag(table))
        .Select(Describe)
        .ToArray();

    private static string Describe(GameScriptEotTable table) => table switch
    {
        GameScriptEotTable.AnimatedObjects => "actor animation",
        GameScriptEotTable.DynamicObjects => "dynamic transform",
        GameScriptEotTable.AiCallbacks => "AI callbacks",
        GameScriptEotTable.CollisionMap => "collision map",
        GameScriptEotTable.CollisionZdm => "ZDM collision",
        GameScriptEotTable.CollisionZde => "ZDE collision",
        GameScriptEotTable.EventObjects => "EVE tracks",
        GameScriptEotTable.HierarchyObjects => "hierarchy updates",
        _ => table.ToString()
    };
}
