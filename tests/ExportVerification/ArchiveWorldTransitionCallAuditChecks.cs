using JadeView;

internal static class ArchiveWorldTransitionCallAuditChecks
{
    internal static void Verify()
    {
        string[] expectedApis =
        [
            "WORLoadList", "WORUniverseAdd", "WORActive", "WORDesactive",
            "WORPlayAbsTime", "WORCurIndexGet", "WORPause", "WORUnPause"
        ];
        Check(expectedApis.All(ArchiveWorldTransitionCallAudit.IsTargetApi)
            && !ArchiveWorldTransitionCallAudit.IsTargetApi("WORUniverseInit")
            && !ArchiveWorldTransitionCallAudit.IsTargetApi("TRAPlay"),
            "The archive audit should stay bounded to the requested world-transition APIs");

        var sourceOwner = new CinematicScriptOwner(0xD9005D6F, 0, 0x71003F6F, 0xFF005833,
            uint.MaxValue, uint.MaxValue, "World scheduler source fixture",
            ["Cine_FigeleMonde_flm_fige"], [], [])
        {
            ModelFunctionNames = ["Cine_FigeleMonde_flm_fige"]
        };
        var sourceCalls = CinematicScriptReader.ReachableEngineCalls(sourceOwner);
        var pause = sourceCalls.FirstOrDefault(call => call.Api == "WORPause");
        Check(pause != null && ArchiveWorldTransitionCallAudit.SelectCalls(sourceOwner)
                .Any(call => call.Api == "WORPause"
                    && call.FunctionName == pause.FunctionName
                    && call.CallbackPath == pause.CallbackPath
                    && call.Source == pause.Source && call.Line == pause.Line
                    && call.BranchConditions.SequenceEqual(pause.BranchConditions)
                    && call.Arguments.SequenceEqual(pause.Arguments)),
            "A real source callback should retain its reachable path, branch evidence, arguments, and source line in the archive audit selection");

        if (pause != null)
        {
            var synthetic = pause with { Api = "WORUniverseAdd" };
            var excluded = pause with { Api = "TRAPlay" };
            var selected = ArchiveWorldTransitionCallAudit.SelectCalls(sourceOwner, [synthetic, excluded]);
            Check(selected.Length == 1 && selected[0].Api == "WORUniverseAdd",
                "Filtering should include requested transition calls and omit unrelated reachable APIs");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
