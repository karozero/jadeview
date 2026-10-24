namespace JadeView;

/// <summary>
/// Models Iris WOR_World_ActivateObjects after a world command requests a table rebuild.
/// The current activated table is replaced; serialized identity flag bit 0 and the
/// source activation predicate determine each new membership entry.
/// </summary>
internal static class GameScriptWorldActivationRebuildReducer
{
    private const uint IdentityExcludesActivation = 0x00000001;
    private const uint RuntimeActive = GameScriptObjectActivation.ActiveStatus;

    internal static GameScriptWorldActivationRebuild Evaluate(
        IReadOnlyList<GameScriptWorldObjectSnapshot> objectTable, bool? specialTime)
    {
        GameScriptWorldActivationObjectState[] states = objectTable
            .Where(item => item.IsValidObjectPointer)
            .Select(item => EvaluateObject(item, specialTime))
            .ToArray();
        bool unresolvedMembership = states.Any(state => state.InActivatedObjectTable is null);
        return new(unresolvedMembership
                ? GameScriptWorldActivationRebuildStatus.PartiallyUnknown
                : GameScriptWorldActivationRebuildStatus.Resolved,
            states,
            "Iris clears the old activated table, clears flags2 bit 0 on valid object-table entries, skips objects with GAO identity bit 0, and rebuilds membership using ENG_gf_SpecialTime.");
    }

    private static GameScriptWorldActivationObjectState EvaluateObject(
        GameScriptWorldObjectSnapshot item, bool? specialTime)
    {
        uint resetFlags2 = item.RuntimeFlags2 & ~RuntimeActive;
        if ((item.Object.Flags & IdentityExcludesActivation) != 0)
            return new(item.Object, item.RuntimeFlags2, resetFlags2, false,
                "GAO identity flag bit 0 excludes this object from WOR_World_ActivateObjects.");

        if (specialTime is { } isSpecialTime)
        {
            bool isActivated = IsEligible(resetFlags2, isSpecialTime);
            uint resultFlags2 = isActivated ? resetFlags2 | RuntimeActive : resetFlags2;
            return new(item.Object, item.RuntimeFlags2, resultFlags2, isActivated,
                isActivated
                    ? "The object passes Iris's activation predicate for the supplied time mode."
                    : "The object does not pass Iris's activation predicate for the supplied time mode.");
        }

        bool eligibleInNormalTime = IsEligible(resetFlags2, specialTime: false);
        bool eligibleInSpecialTime = IsEligible(resetFlags2, specialTime: true);
        if (eligibleInNormalTime == eligibleInSpecialTime)
        {
            uint resultFlags2 = eligibleInNormalTime ? resetFlags2 | RuntimeActive : resetFlags2;
            return new(item.Object, item.RuntimeFlags2, resultFlags2, eligibleInNormalTime,
                "Normal-time and special-time activation predicates produce the same membership.");
        }

        return new(item.Object, item.RuntimeFlags2, null, null,
            "ENG_gf_SpecialTime is unavailable and normal-time versus special-time activation differs.");
    }

    private static bool IsEligible(uint runtimeFlags2, bool specialTime) =>
        GameScriptObjectActivation.Evaluate(runtimeFlags2, specialTime)
            == GameScriptObjectActivationResult.Eligible;
}

internal enum GameScriptWorldActivationRebuildStatus
{
    Resolved,
    PartiallyUnknown
}

internal sealed record GameScriptWorldActivationObjectState(
    SceneObject Object,
    uint PreviousFlags2,
    uint? ResultFlags2,
    bool? InActivatedObjectTable,
    string Explanation);

internal sealed record GameScriptWorldActivationRebuild(
    GameScriptWorldActivationRebuildStatus Status,
    GameScriptWorldActivationObjectState[] Objects,
    string Explanation);
