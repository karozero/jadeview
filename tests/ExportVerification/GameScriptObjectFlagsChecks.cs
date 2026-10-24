using JadeView;

internal static class GameScriptObjectFlagsChecks
{
    internal static void Verify()
    {
        Check(CinematicEngineFunctionCatalog.NameFor(0x1FB3) == "OBJ_FlagsControlSet"
            && CinematicEngineFunctionCatalog.NameFor(0x1FD8) == "OBJ_FlagInvisibleSet"
            && CinematicEngineFunctionCatalog.NameFor(0x1FD9) == "OBJ_FlagInactiveSet"
            && CinematicEngineFunctionCatalog.NameFor(0x3BC4) == "GRPInvisibleSet"
            && CinematicEngineFunctionCatalog.NameFor(0x3BC5) == "GRPInactiveSet",
            "Object flag handlers drifted from the embedded engine-function selector catalog.");

        const uint receiver = 0x1234;
        uint before = GameObjectRuntimeFlags.Inactive | 0xABCD;
        var clearInactive = GameScriptObjectFlags.Decode(receiver, 0x1FD9, [0])!;
        Check(clearInactive.TargetKey == receiver && !clearInactive.TargetsGroup,
            "An object-state command changed its resolved receiver or dispatch scope.");
        Check(GameObjectRuntimeFlags.Apply(before, clearInactive.SetMask, clearInactive.ClearMask) == 0xABCD,
            "Clearing ForceInactive changed the low status word.");

        var hideGroup = GameScriptObjectFlags.Decode(receiver, 0x3BC4, [uint.MaxValue])!;
        Check(hideGroup.TargetsGroup && hideGroup.SetMask == GameObjectRuntimeFlags.Invisible,
            "Group invisibility lost explicit group dispatch or nonzero integer boolean semantics.");
        uint hidden = GameObjectRuntimeFlags.Apply(before, hideGroup.SetMask, hideGroup.ClearMask);
        Check((hidden & GameObjectRuntimeFlags.Inactive) != 0,
            "Changing visibility unexpectedly changed activity.");

        var overlapping = GameScriptObjectFlags.Decode(receiver, 0x1FB3,
            [GameObjectRuntimeFlags.Invisible | 0xFFFF, GameObjectRuntimeFlags.Invisible | 0xFFFF])!;
        Check(GameObjectRuntimeFlags.Apply(0x1234, overlapping.SetMask, overlapping.ClearMask)
                == (GameObjectRuntimeFlags.Invisible | 0x1234),
            "FlagsControlSet failed to preserve the status word or give set bits precedence over clear bits.");
        Check(GameScriptObjectFlags.Decode(receiver, 0x1FD8, []) == null
            && GameScriptObjectFlags.Decode(receiver, 0x3BC5, [0, 1]) == null
            && GameScriptObjectFlags.Decode(receiver, 0x1FB1, []) == null,
            "Malformed flag calls or object destruction were treated as a complete flag-only operation.");
        Console.WriteLine("PASS: shared script flag semantics preserve status bits, set precedence and activity/visibility separation");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
