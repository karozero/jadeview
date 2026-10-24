using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private static void DrawCinematicCallbackRegistrationCandidates(CinematicScriptOwner owner,
        CinematicScriptReachableSourceReference[] sourceReferences)
    {
        CinematicScriptReachableEngineCall[] reachableCalls =
            CinematicScriptReader.ReachableEngineCalls(owner);
        CinematicScriptCallbackRegistrationCandidate[] candidates =
            CinematicScriptCallbackRegistrationAnalyzer.Analyze(
                owner, reachableCalls, sourceReferences);
        CinematicScriptCallbackResetCandidate[] resetCandidates =
            CinematicScriptCallbackRegistrationAnalyzer.AnalyzeResetSites(
                owner, reachableCalls);
        if (candidates.Length == 0 && resetCandidates.Length == 0) return;

        ImGui.Separator();
        if (candidates.Length > 0
            && ImGui.CollapsingHeader($"Dynamic AI callback registration candidates ({candidates.Length})"))
        {
            ImGui.TextDisabled("AICBAdd/AICBDel edit a mutable callback list on a destination AI instance. Static exact-tuple matches below are candidate callsites only; branches, call order, initialization, and runtime execution are not simulated.");
            foreach (var candidate in candidates.Take(48))
                DrawCandidate(candidate);
            if (candidates.Length > 48)
                ImGui.TextDisabled($"Showing 48 of {candidates.Length} candidate sites.");
        }

        if (resetCandidates.Length > 0
            && ImGui.CollapsingHeader($"Potential AI instance reset sites (AIReinitVars) ({resetCandidates.Length})"))
        {
            ImGui.TextDisabled("Jaded and Iris show AIReinitVars reinitializing the current GAO's AI instance and clearing its dynamic callback list (except the universe instance). These sites are listed separately because they do not take the callback destination as an argument; a matching registration reset is not inferred here.");
            foreach (CinematicScriptCallbackResetCandidate reset in resetCandidates.Take(32))
            {
                ImGui.BulletText($"Possible current-instance reset · {reset.Source}:{reset.Line}");
                if (reset.BranchConditions.Length > 0)
                    ImGui.TextDisabled($"Source guards: {string.Join("; ", reset.BranchConditions)}");
                foreach (string finding in reset.SerializedBranchFindings)
                    ImGui.TextWrapped($"Serialized branch evidence: {finding}");
                ImGui.TextDisabled($"Reachable path: {reset.CallbackPath}");
            }
            if (resetCandidates.Length > 32)
                ImGui.TextDisabled($"Showing 32 of {resetCandidates.Length} reset sites.");
        }
    }

    private static void DrawCandidate(CinematicScriptCallbackRegistrationCandidate candidate)
    {
        string callbackId = candidate.CallbackType is { } numericId
            ? $"ID {numericId}"
            : $"type/id {candidate.CallbackTypeOperand}";
        string title = $"{candidate.Operation} · {callbackId} · {candidate.FunctionName} · {candidate.Source}:{candidate.Line}";
        ImGui.BulletText(title);
        ImGui.TextDisabled($"Context/source instance: {candidate.CallbackSourceContext}");
        ImGui.TextDisabled($"Destination AI instance: {candidate.DestinationInstance}");
        if (!string.IsNullOrEmpty(candidate.FunctionOperand)
            && candidate.FunctionOperand != candidate.FunctionName)
        {
            string key = candidate.FunctionKey is { } functionKey
                ? $" · key {functionKey:X8}"
                : " · function key unresolved";
            ImGui.TextDisabled($"Function operand: {candidate.FunctionOperand} → {candidate.FunctionName}{key}");
        }
        if (candidate.BranchConditions.Length > 0)
            ImGui.TextDisabled($"Source guards: {string.Join("; ", candidate.BranchConditions)}");
        foreach (string finding in candidate.SerializedBranchFindings)
            ImGui.TextWrapped($"Serialized branch evidence: {finding}");
        ImGui.TextDisabled($"Reachable path: {candidate.CallbackPath}");

        if (candidate.Operation == "Register candidate")
        {
            if (candidate.MatchingRemovalSites.Length > 0)
                ImGui.TextDisabled($"Exact resolved tuple also appears at AICBDel site(s): {string.Join(", ", candidate.MatchingRemovalSites)}. This is a source-level match; it does not establish order or execution.");
            else
                ImGui.TextDisabled("No AICBDel site with the same uniquely resolved context, destination, literal ID, and function identity was found.");
        }

        if (candidate.CallbackType is not { } callbackIdValue) return;
        var dispatchSites = CinematicScriptReader.EngineDispatchSitesFor(callbackIdValue);
        if (dispatchSites.Length == 0)
        {
            ImGui.TextDisabled($"No indexed Iris engine dispatch site for callback ID {callbackIdValue}.");
            return;
        }

        foreach (CinematicScriptCallbackDispatchSite site in dispatchSites.Take(6))
            DrawCinematicCallbackDispatchSite(site);
        if (dispatchSites.Length > 6)
            ImGui.TextDisabled($"Showing 6 of {dispatchSites.Length} indexed Iris call sites.");
    }
}
