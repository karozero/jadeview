using JadeView;

internal static class CinematicScriptEngineCallIndexChecks
{
    internal static void Verify()
    {
        var owner = new CinematicScriptOwner(0x10004AE8, 0, 0, 0xFF0045A2,
            0, 0, "source-index fixture", [], [], [])
        {
            ModelFunctionNames = ["sfx_bouclier_phare_sbp_disparition"]
        };

        CinematicScriptReachableEngineCall materialSetter =
            CinematicScriptReader.ConfiguredModelEngineCalls(owner)
                .Single(call => call.Api == "MATSetConstant" && call.Line == 8486);
        CinematicScriptArgumentAssignmentAlternative[] alternatives =
            (materialSetter.ArgumentAssignmentAlternatives ?? [])
            .Where(alternative => alternative.Variable == "v35")
            .ToArray();

        Check(alternatives.Any(alternative => alternative.Expression == "v22"
                && HasBranch(alternative, "if", "v34->i_cine"))
            && alternatives.Any(alternative => alternative.Expression == "v25"
                && HasBranch(alternative, "else", "v34->i_cine")),
            "The embedded source-call index must preserve both branch-specific reaching values for MATSetConstant.");

        CinematicScriptReachableEngineCall colorBlend =
            CinematicScriptReader.ConfiguredModelEngineCalls(owner)
                .Single(call => call.Api == "COLORBlend" && call.Line == 8483);
        Check(HasBranch(colorBlend, "else", "v34->i_cine"),
            "Nested engine calls must retain the branch provenance of the expression that invokes them.");

        CinematicScriptOvaFieldWrite transparencyWrite =
            (materialSetter.OvaFieldWrites ?? [])
                .Single(write => write.VariableName == "f_transparency_coef");
        CinematicScriptArgumentAssignmentAlternative[] fadeInputs =
            (transparencyWrite.ValueAssignmentAlternatives ?? []).ToArray();
        Check(transparencyWrite.Expression == "2.0f - v16"
            && HasBranch(transparencyWrite.BranchConditions, "if", "!v34->i_cine")
            && fadeInputs.Any(input => input.Variable == "v8"
                && input.Expression == "(v20 - v34->f_time) / v34->des_f_timed")
            && fadeInputs.Any(input => input.Variable == "v8" && input.Expression == "0.0")
            && fadeInputs.Any(input => input.Variable == "v8" && input.Expression == "1.0"),
            "The embedded index must trace instance-data pointer aliases and preserve the callback's clamped time-based field update.");

        CinematicScriptOwner aircraftOwner = owner with
        {
            ModelFunctionNames = ["CV_aircraft_CV_exec_before_display"]
        };
        CinematicScriptReachableEngineCall alphaSetter =
            CinematicScriptReader.ConfiguredModelEngineCalls(aircraftOwner)
                .Single(call => call.Api == "SetLocalAlpha" && call.Line == 2882);
        CinematicScriptArgumentAssignmentSource alphaFactor =
            alphaSetter.ArgumentAssignments.Single(assignment => assignment.Variable == "v4");
        CinematicScriptArgumentAssignmentSource waterFactor =
            alphaSetter.ArgumentAssignments.Single(assignment => assignment.Variable == "v3");
        CinematicScriptRliValueFlowAssignment[] localAlphaAssignments =
            CinematicScriptLocalAlphaPlayback.ValueFlowAssignmentsForCall(alphaSetter, []);
        CinematicScriptArgumentAssignmentAlternative[] waterFactorAlternatives =
            (alphaSetter.ArgumentAssignmentAlternatives ?? [])
                .Where(alternative => alternative.Variable == "v3")
                .ToArray();
        Check(HasBranch(alphaFactor.BranchConditions ?? [], "if", "v1->i_flag_on_coule")
            && !waterFactor.IsUnique
            && localAlphaAssignments.Any(assignment => assignment.Variable == "v4"
                && HasBranch(assignment.BranchConditions, "if", "v1->i_flag_on_coule"))
            && waterFactorAlternatives.Any(alternative => alternative.Expression == "0.0"
                && HasBranch(alternative, "if", "v3 <= 1.0")
                && HasBranch(alternative, "if", "v3 < 0.0"))
            && waterFactorAlternatives.Any(alternative => alternative.Expression == "1.0"
                && HasBranch(alternative, "else", "v3 <= 1.0"))
            && localAlphaAssignments.Where(assignment => assignment.Variable == "v3")
                .All(assignment => assignment.BranchConditions.Length > 0),
            "SetLocalAlpha must preserve the guards on unique and branch-specific reaching values, excluding only unguarded ambiguous assignments.");

        Console.WriteLine("PASS: embedded script-call index preserves branch-specific reaching definitions");
    }

    private static bool HasBranch(CinematicScriptArgumentAssignmentAlternative alternative,
        string kind, string expression) => alternative.BranchConditions.Any(branch =>
            branch.Kind == kind && branch.Expression == expression);

    private static bool HasBranch(CinematicScriptReachableEngineCall call, string kind,
        string expression) => call.BranchConditions.Any(branch =>
        branch.Kind == kind && branch.Expression == expression);

    private static bool HasBranch(IEnumerable<CinematicScriptBranchConditionSource> branches,
        string kind, string expression) => branches.Any(branch =>
        branch.Kind == kind && branch.Expression == expression);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
