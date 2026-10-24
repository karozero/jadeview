namespace JadeView;

internal static class CinematicStartCallbackChain
{
    internal const string FirstInit = "Cine_Events_cine_firstinit";
    internal const string Initialize = "Cine_Events_cine_init";
    internal const string DepartureTest = "Cine_Events_cine_tests_depart";

    private static readonly HashSet<string> FunctionNames =
    [
        FirstInit,
        Initialize,
        DepartureTest
    ];

    internal static bool IsReferenced(CinematicScriptOwner owner) =>
        owner.CallbackNames.Any(FunctionNames.Contains)
        || owner.ModelFunctionNames.Any(FunctionNames.Contains);

    internal static string[] AssignedFunctions(CinematicScriptOwner owner) =>
        owner.CallbackNames.Where(FunctionNames.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    internal static string DescribeFlow(IEnumerable<string> assignedFunctions)
    {
        string[] assigned = assignedFunctions.ToArray();
        if (assigned.Contains(DepartureTest, StringComparer.Ordinal))
            return $"The effective callback enters {DepartureTest} directly.";
        if (assigned.Contains(Initialize, StringComparer.Ordinal))
            return $"The effective callback enters {Initialize}, which schedules {DepartureTest}.";
        if (assigned.Contains(FirstInit, StringComparer.Ordinal))
            return $"The effective callback enters {FirstInit}, then {Initialize}, which schedules {DepartureTest}.";
        return "The Cine_Events departure callback is referenced by the model, but no startup-chain entry point is assigned to an effective callback slot.";
    }
}
