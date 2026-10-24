namespace JadeView;

/// <summary>
/// Forecasts Iris's initial activated-object-table membership from serialized GAO identity
/// and state flags, including the outer world-rebuild identity-bit filter. The result is
/// conditional and never represents observed runtime state.
/// </summary>
internal static class GameScriptInitialActivationForecaster
{
    private const uint WorldRebuildIdentitySkipBit = 0x00000001;
    private const uint NormalTimeActivationBlockMask = GameScriptObjectActivation.NormalTimeBlockMask;
    private const uint SpecialTimeActivationBit = GameScriptObjectActivation.SpecialTimeActivationBit;
    private const string NormalTime = "ENG_gf_SpecialTime == 0";
    private const string SpecialTime = "ENG_gf_SpecialTime != 0";

    internal static GameScriptInitialActivationForecast Evaluate(
        uint identityFlags, uint serializedStateFlags)
    {
        uint initialRuntimeFlags2 = GameScriptSerializedObjectState
            .ProjectInitialFlags2(serializedStateFlags);
        bool skippedByWorldRebuild = (identityFlags & WorldRebuildIdentitySkipBit) != 0;
        var membership = GameScriptEotMembershipResolver.Evaluate(identityFlags);
        var paths = new[]
        {
            EvaluateNormalTimePath(initialRuntimeFlags2, skippedByWorldRebuild, membership),
            EvaluateSpecialTimePath(initialRuntimeFlags2, skippedByWorldRebuild, membership)
        };

        return new(serializedStateFlags, initialRuntimeFlags2, paths,
            $"Conditional initial activation forecast only (initial flags2 bit 0x1 is {DescribeActivityBit(initialRuntimeFlags2)}); "
                + "raw serialized bits are projected by Iris's GAO loader, not copied directly. Assumes a freshly loaded object "
                + "and no later runtime flag changes; WOR_World_ActivateObjects skips identity flag bit 0x1 before testing flags2. "
                + "Other identity bits select EOT scheduler categories. "
                + "ENG_gf_SpecialTime is unknown; this is not observed world membership.");
    }

    private static GameScriptActivationPathForecast EvaluateNormalTimePath(uint initialFlags2,
        bool skippedByWorldRebuild, GameScriptEotMembership membership)
    {
        if (skippedByWorldRebuild)
            return Blocked(NormalTime,
                "WOR_World_ActivateObjects skips GAO identity flag bit 0x1 before the flags2 activation predicate.");

        GameScriptObjectActivationResult activation = GameScriptObjectActivation
            .Evaluate(initialFlags2, specialTime: false);
        if (activation == GameScriptObjectActivationResult.NormalTimeControlBlocked)
            return Blocked(NormalTime,
                $"Initial flags2 intersects the normal-time activation block mask 0x{NormalTimeActivationBlockMask:X8}.");
        if (activation == GameScriptObjectActivationResult.NormalTimeStatusBlocked)
            return Blocked(NormalTime, "Initial flags2 bit 0x10 blocks normal-time activation.");

        return Eligible(NormalTime,
            "The initial flags2 snapshot passes Iris's normal-time activation masks.", membership);
    }

    private static GameScriptActivationPathForecast EvaluateSpecialTimePath(uint initialFlags2,
        bool skippedByWorldRebuild, GameScriptEotMembership membership)
    {
        if (skippedByWorldRebuild)
            return Blocked(SpecialTime,
                "WOR_World_ActivateObjects skips GAO identity flag bit 0x1 before the flags2 activation predicate.");

        if (GameScriptObjectActivation.Evaluate(initialFlags2, specialTime: true)
            == GameScriptObjectActivationResult.SpecialTimeBitMissing)
            return Blocked(SpecialTime,
                $"Initial flags2 lacks the special-time activation bit 0x{SpecialTimeActivationBit:X8}.");

        return Eligible(SpecialTime,
            "Initial flags2 contains the required special-time activation bit.", membership);
    }

    private static GameScriptActivationPathForecast Eligible(string worldMode, string reason,
        GameScriptEotMembership membership) =>
        new(worldMode, GameScriptActivationEligibility.Eligible, reason, membership);

    private static GameScriptActivationPathForecast Blocked(string worldMode, string reason) =>
        new(worldMode, GameScriptActivationEligibility.NotEligible, reason);

    private static string DescribeActivityBit(uint initialFlags2) =>
        (initialFlags2 & GameScriptObjectActivation.ActiveStatus) != 0 ? "set" : "clear";
}
