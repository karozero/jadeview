using JadeView;

internal static class GameScriptEotMembershipChecks
{
    internal static void Verify()
    {
        VerifyInitialActivationProjectionAndSchedulerMembership();

        const uint flags = 0x02000000 | 0x20 | 0x2 | 0x400000;
        GameScriptEotMembership membership = GameScriptEotMembershipResolver.Evaluate(flags);

        Require(membership.IdentityFlags == flags
            && membership.Tables.HasFlag(GameScriptEotTable.EventObjects)
            && membership.Tables.HasFlag(GameScriptEotTable.AiCallbacks)
            && membership.Tables.HasFlag(GameScriptEotTable.AnimatedObjects)
            && membership.Tables.HasFlag(GameScriptEotTable.HierarchyObjects)
            && membership.TableNames.Contains("EVE tracks")
            && membership.TableNames.Contains("AI callbacks"),
            "Iris EOT membership must derive each scheduler table from its GAO identity flag.");

        GameScriptEotMembership unknownIdentityBits = GameScriptEotMembershipResolver.Evaluate(0x1);
        Require(GameScriptEotMembershipResolver.Evaluate(0).TableNames.Length == 0
            && unknownIdentityBits.IdentityFlags == 0x1
            && unknownIdentityBits.Tables == GameScriptEotTable.None,
            "Non-EOT identity bits must be retained as raw evidence but excluded from scheduler-table membership.");
    }

    private static void VerifyInitialActivationProjectionAndSchedulerMembership()
    {
        const uint identityFlags = (uint)(GameScriptEotTable.EventObjects | GameScriptEotTable.AiCallbacks);
        GameScriptInitialActivationForecast forecast = GameScriptInitialActivationForecaster
            .Evaluate(identityFlags, serializedStateFlags: 0);
        GameScriptInitialActivationForecast inactiveForecast = GameScriptInitialActivationForecaster
            .Evaluate(identityFlags, serializedStateFlags: 0x40000000);
        GameScriptInitialActivationForecast identityExcluded = GameScriptInitialActivationForecaster
            .Evaluate(identityFlags | 1, serializedStateFlags: 0);

        Require(forecast.InitialRuntimeFlags2 == 0
            && forecast.Paths[0].Eligibility == GameScriptActivationEligibility.Eligible
            && forecast.Paths[0].EotMembership?.TableNames.Contains("EVE tracks") == true
            && forecast.Paths[0].EotMembership?.TableNames.Contains("AI callbacks") == true
            && forecast.Paths[1].Eligibility == GameScriptActivationEligibility.NotEligible
            && inactiveForecast.InitialRuntimeFlags2 == 0x40400000
            && inactiveForecast.Paths[0].Eligibility == GameScriptActivationEligibility.NotEligible
            && inactiveForecast.Paths[1].Eligibility == GameScriptActivationEligibility.NotEligible
            && identityExcluded.Paths.All(path => path.Eligibility == GameScriptActivationEligibility.NotEligible
                && path.EotMembership == null),
            "Initial world rebuild must apply its identity-bit exclusion before flags2 activation and EOT membership. "
            + $"normal={forecast.Paths[0]}, special={forecast.Paths[1]}, inactive flags2=0x{inactiveForecast.InitialRuntimeFlags2:X8}, "
            + $"inactive normal={inactiveForecast.Paths[0].Eligibility}, inactive special={inactiveForecast.Paths[1].Eligibility}, "
            + $"identity-bit normal={identityExcluded.Paths[0]}, identity-bit special={identityExcluded.Paths[1]}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
