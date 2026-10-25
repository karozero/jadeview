using JadeView;

internal static class CinematicScriptRliValueFlowIndexChecks
{
    internal static void Verify()
    {
        const string functionName = "sfx_bouclier_phare_abp_apparition2";
        var owner = new CinematicScriptOwner(0x100, 0x200, 0x300, 0x400,
            0x500, 0x600, "RLI value-flow fixture", [functionName], [], [])
        {
            ModelFunctionNames = [functionName]
        };

        CinematicScriptRliValueFlowAssignment[] assignments =
            CinematicScriptReader.ReachableRliValueFlowAssignments(owner);
        CinematicScriptReachableEngineCall? channelGetter =
            CinematicScriptReader.ReachableEngineCalls(owner).SingleOrDefault(call =>
                call.FunctionName == functionName && call.Line == 8139);
        CinematicScriptRliValueFlowAssignment? channelRead = assignments.SingleOrDefault(
            assignment => assignment.Variable == "a3a"
                && assignment.Expression == "AI_EvalFunc_OBJ_GetChannelProg_C(AI_gpst_CurrentGameObject, 0)");
        CinematicScriptRliValueFlowAssignment? factorWrite = assignments.SingleOrDefault(
            assignment => assignment.Variable == "f_transparency_coef"
                && assignment.Line == 8143
                && assignment.Expression == "1.0f - v16");
        CinematicScriptRliValueFlowAssignment? gameplayBranchWrite = assignments.SingleOrDefault(
            assignment => assignment.Variable == "f_transparency_coef"
                && assignment.Line == 8185
                && assignment.Expression == "1.29f - a3");
        Check(channelGetter?.Api == "OBJ_GetChannelProg"
            && channelGetter.ResultVariable == "a3a"
            && channelRead != null && factorWrite != null
            && channelRead.CallbackPath.Length > 0
            && factorWrite.CallbackPath.Length > 0
            && channelRead.BranchConditions.Length == 1
            && factorWrite.BranchConditions.Length == 1
            && channelRead.BranchConditions[0].Expression == "v29->i_cine"
            && gameplayBranchWrite?.BranchConditions is
                [{ Kind: "else", Expression: "v29->i_cine" }],
            "Reachable RLI scalar-flow assignments must retain channel reads, ordered OVA writes, callback paths, and branch guards.");

        Console.WriteLine("PASS: embedded RLI scalar value-flow index retains callback-local channel inputs and OVA writes");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
