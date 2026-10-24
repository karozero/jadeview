using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
    private readonly Dictionary<CinematicScriptOwnerId, CinematicScriptAiTrackMutationCandidate[]>
        _cinematicAiTrackMutationCache = [];

    private void DrawCinematicScriptAiTrackMutationCandidates(CinematicScriptOwner owner)
    {
        CinematicScriptAiTrackMutationCandidate[] candidates = GetCinematicAiTrackMutationCandidates(owner);

        DrawCategoryTitle($"Source-reachable track mutation candidates ({candidates.Length})");
        if (candidates.Length == 0)
        {
            ImGui.TextDisabled("No supported AI track mutation calls were found in the source call graph.");
            return;
        }
        if (!ImGui.TreeNode($"Possible AI track transitions ({candidates.Length})")) return;

        ImGui.TextDisabled("These are static source-path possibilities, not executed transitions. Slot numbers are candidates only; direct engine callbacks, runtime order, timers, AI admission, and branch results may remain unknown.");
        foreach (CinematicScriptAiTrackMutationCandidate candidate in candidates)
        {
            string function = candidate.FunctionKey is { } key
                ? $"{candidate.FunctionDescription} ({key:X8})"
                : candidate.FunctionDescription;
            ImGui.BulletText($"{candidate.Api}: {candidate.TrackDescription} → {function}");
            ImGui.TextWrapped(candidate.Effect);
            ImGui.TextDisabled($"{candidate.Source}:{candidate.Line} · {candidate.CallbackPath}");
            foreach (string branch in candidate.BranchConditions)
                ImGui.TextDisabled(branch);
            foreach (string finding in candidate.SerializedBranchFindings)
                ImGui.TextDisabled(finding);
        }
        ImGui.TreePop();
    }

    private CinematicScriptAiTrackMutationCandidate[] GetCinematicAiTrackMutationCandidates(
        CinematicScriptOwner owner)
    {
        if (_cinematicAiTrackMutationCache.TryGetValue(owner.Id, out var cached)) return cached;

        CinematicScriptReachableEngineCall[] reachableCalls = CinematicScriptReader.ReachableEngineCalls(owner);
        CinematicScriptAiTrackMutationCandidate[] candidates = CinematicScriptAiTrackMutationAnalyzer.Analyze(
            owner, reachableCalls, CinematicScriptReader.ReachableSourceReferences(owner));
        _cinematicAiTrackMutationCache[owner.Id] = candidates;
        return candidates;
    }
}
