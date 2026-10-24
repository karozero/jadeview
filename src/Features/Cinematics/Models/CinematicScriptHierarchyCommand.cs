namespace JadeView;

internal enum CinematicScriptHierarchyCommandKind
{
    SetParent,
    Reset
}

internal sealed record CinematicScriptHierarchyCommand(
    float Time,
    uint ChildKey,
    CinematicScriptHierarchyCommandKind Kind,
    uint? ParentKey)
{
    private const int HierarchySetSelector = 0x1fdc;
    private const int HierarchyResetSelector = 0x1fdd;

    internal static CinematicScriptHierarchyCommand? Decode(float time, CinematicLiteralCall call)
    {
        if (!float.IsFinite(time) || call.Target is 0 or uint.MaxValue) return null;

        if (call.Selector == HierarchyResetSelector && call.Arguments.Length == 0)
            return new(time, call.Target, CinematicScriptHierarchyCommandKind.Reset, null);

        if (call.Selector != HierarchySetSelector || call.Arguments.Length != 1) return null;

        uint parentKey = call.Arguments[0];
        bool parentIsNull = parentKey is 0 or uint.MaxValue || parentKey == call.Target;
        return new(time, call.Target, CinematicScriptHierarchyCommandKind.SetParent,
            parentIsNull ? null : parentKey);
    }
}
