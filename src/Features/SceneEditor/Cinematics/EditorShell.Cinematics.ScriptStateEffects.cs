using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private static void DrawCinematicScriptCallbackStateEffects(CinematicScriptOwner owner)
    {
        if (!ImGui.CollapsingHeader("Callback state-effect candidates")) return;

        CinematicScriptCallbackStateEffect[] candidates =
            CinematicScriptCallbackStateEffectAnalyzer.Analyze(owner);
        if (candidates.Length == 0)
        {
            ImGui.TextDisabled("No reachable visibility, activity, capability, destroy, position, or hierarchy calls were found.");
            return;
        }

        ImGui.Indent();
        ImGui.TextWrapped("Reachable Iris callback source contains possible visibility, activity, capability, destruction, position, or hierarchy effects. Recognized identity-flag predicates are compared individually with serialized GAO flags; nested branch conditions are not combined. Callbacks are not timed or executed by JadeView, and runtime activation may remain unresolved.");
        foreach (IGrouping<string, CinematicScriptCallbackStateEffect> apiGroup in candidates
                     .GroupBy(candidate => candidate.Api)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            string apiHeader = $"{apiGroup.Key} · {apiGroup.Count()} candidate(s)##callback-effects-{owner.OwnerKey:X8}-{apiGroup.Key}";
            if (!ImGui.TreeNode(apiHeader)) continue;

            foreach (IGrouping<string, CinematicScriptCallbackStateEffect> functionGroup in apiGroup
                         .GroupBy(candidate => candidate.FunctionName)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                string functionHeader = $"{functionGroup.Key} · {functionGroup.Count()} call(s)##callback-function-{owner.OwnerKey:X8}-{apiGroup.Key}-{functionGroup.Key}";
                if (!ImGui.TreeNode(functionHeader)) continue;

                foreach (CinematicScriptCallbackStateEffect candidate in functionGroup
                             .OrderBy(item => item.Source, StringComparer.Ordinal)
                             .ThenBy(item => item.Line))
                    DrawCallbackStateEffect(candidate);

                ImGui.TreePop();
            }

            ImGui.TreePop();
        }
        ImGui.Unindent();
    }

    private static void DrawCallbackStateEffect(CinematicScriptCallbackStateEffect candidate)
    {
        ImGui.BulletText($"{candidate.StateEffect} · {candidate.OwnerKey:X8}");
        ImGui.Indent();
        ImGui.TextDisabled($"{candidate.Api}{(candidate.Variant.Length == 0 ? "" : $"_{candidate.Variant}")}({string.Join(", ", candidate.Arguments)})");
        ImGui.TextWrapped(candidate.ReceiverKey is { } receiverKey
            ? $"Receiver: {candidate.ReceiverEvidence} · {receiverKey:X8}"
            : $"Receiver unresolved: {candidate.ReceiverEvidence}");
        ImGui.TextDisabled($"{Path.GetFileName(candidate.Source)}:{candidate.Line}");
        ImGui.TextWrapped($"Callback path: {candidate.CallbackPath}");
        if (candidate.BranchConditions.Length == 0)
            ImGui.TextDisabled("No enclosing source branch was recorded.");
        else
            foreach (CinematicScriptBranchConditionSource branch in candidate.BranchConditions)
                ImGui.TextWrapped($"{(branch.Kind == "else" ? "else of " : "if ")}{branch.Expression} · line {branch.Line}");
        foreach (string finding in candidate.SerializedBranchFindings)
            ImGui.TextWrapped($"Serialized branch evidence: {finding}");
        ImGui.TextDisabled(candidate.ExecutionStatus);
        ImGui.Unindent();
    }
}
