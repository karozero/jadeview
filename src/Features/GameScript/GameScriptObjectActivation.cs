namespace JadeView;

/// <summary>
/// Iris world.c:44-62 predicate for adding an object to the active-object table.
/// This requires runtime flags and known special-time mode, not raw archive flags.
/// Jaded's later sector-based predicate is a different implementation.
/// </summary>
internal static class GameScriptObjectActivation
{
    internal const uint ActiveStatus = 0x1;
    internal const uint NormalTimeBlockMask = 0x04400000;
    internal const uint NormalTimeStatusBlock = 0x10;
    internal const uint SpecialTimeActivationBit = 0x04000000;

    internal static GameScriptObjectActivationResult Evaluate(uint runtimeFlags, bool? specialTime)
    {
        if ((runtimeFlags & ActiveStatus) != 0)
            return GameScriptObjectActivationResult.AlreadyActive;
        if (specialTime == null)
            return GameScriptObjectActivationResult.TimeModeUnknown;
        if (specialTime.Value)
            return (runtimeFlags & SpecialTimeActivationBit) != 0
                ? GameScriptObjectActivationResult.Eligible
                : GameScriptObjectActivationResult.SpecialTimeBitMissing;
        if ((runtimeFlags & NormalTimeBlockMask) != 0)
            return GameScriptObjectActivationResult.NormalTimeControlBlocked;
        return (runtimeFlags & NormalTimeStatusBlock) != 0
            ? GameScriptObjectActivationResult.NormalTimeStatusBlocked
            : GameScriptObjectActivationResult.Eligible;
    }
}
