using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawCinematicScriptActionKitMerges(CinematicScriptOwner owner)
    {
        var analysis = CinematicScriptActionKitMergeResolver.Analyze(owner,
            CinematicScriptReader.ReachableEngineCalls(owner), Document!.CinematicActionKitsByOwner);
        if (analysis.Resolved.Length == 0 && analysis.Unresolved.Length == 0) return;

        if (analysis.Resolved.Length > 0)
        {
            EditorUi.DrawCategoryTitle($"Callback action-kit merges ({analysis.Resolved.Length})");
            ImGui.TextDisabled("Source-confirmed ACTKitMerge calls are joined to serialized OVA object references and decoded action tables. This shows configured slot transfers, not that a callback ran.");
            foreach (var merge in analysis.Resolved)
            {
                bool overwrites = merge.Mode == "OVERWRITE";
                string functionName = overwrites ? "ACTKitMergeOver" : "ACTKitMergeNoOver";
                string sourceWorld = merge.SourceWorldKey == owner.WorldKey
                    ? "owner world" : $"world {merge.SourceWorldKey:X8}";
                string destinationWorld = merge.DestinationWorldKey == owner.WorldKey
                    ? "owner world" : $"world {merge.DestinationWorldKey:X8}";
                string slotEffect = overwrites
                    ? $"{merge.DestinationConflictCount} destination conflicts replaced"
                    : $"{merge.DestinationConflictCount} existing slots retained";
                ImGui.BulletText($"{functionName} · {merge.SourceKey:X8} ({sourceWorld}) → {merge.DestinationKey:X8} ({destinationWorld}) · slots {merge.FirstAction}–{merge.LastAction} · {merge.SourceSlotCount} source slots · {slotEffect}");
                ImGui.TextWrapped($"  destination: {merge.DestinationBinding} · source: {merge.SourceBinding} · range: {merge.RangeBinding}");
                ImGui.TextWrapped($"  {Path.GetFileName(merge.SourceCall.Source)}:{merge.SourceCall.Line} · {merge.SourceCall.CallbackPath}");
                DrawSourceBranchConditions(merge.SourceCall);
            }
        }

        if (analysis.Unresolved.Length > 0
            && ImGui.CollapsingHeader($"Unresolved callback action-kit merges ({analysis.Unresolved.Length})"))
        {
            foreach (var merge in analysis.Unresolved)
            {
                var call = merge.SourceCall;
                ImGui.BulletText($"ACTKitMerge{(call.Variant == "OVERWRITE" ? "Over" : "NoOver")}({string.Join(", ", call.Arguments)})");
                ImGui.TextWrapped($"  {Path.GetFileName(call.Source)}:{call.Line} · {call.CallbackPath} · unresolved: {merge.Reason}");
                DrawSourceBranchConditions(call);
            }
        }

        static void DrawSourceBranchConditions(CinematicScriptReachableEngineCall call)
        {
            if (call.BranchConditions.Length == 0) return;
            string branch = string.Join("; ", call.BranchConditions.Select(condition =>
                condition.Kind == "else" ? $"else of ({condition.Expression})" : $"if ({condition.Expression})"));
            ImGui.TextWrapped($"  source branch: {branch}");
        }
    }

    private static void DrawCinematicScriptActionRequests(CinematicScriptOwner owner,
        IReadOnlyDictionary<int, CinematicAction> actions)
    {
        var analysis = CinematicScriptActionRequestResolver.Analyze(owner,
            CinematicScriptReader.ReachableEngineCalls(owner), actions);
        if (analysis.Resolved.Length == 0 && analysis.Unresolved.Length == 0) return;

        if (analysis.Resolved.Length > 0)
        {
            EditorUi.DrawCategoryTitle($"Callback action requests ({analysis.Resolved.Length})");
            ImGui.TextDisabled("Source-reachable ACTActionSet, ACTActionFrameSet and ACTActionFrequencySet calls are joined to this owner's decoded action table. They show configured requests and branch conditions, not the action active in a live game session.");
            foreach (var request in analysis.Resolved)
            {
                var call = request.SourceCall;
                string clipItems = request.Action.Items.Length == 0
                    ? "no decoded animation items"
                    : string.Join(", ", request.Action.Items.Select(item =>
                        $"{item.Clip.Key:X8} (shape {FormatScriptKey(item.ShapeKey)})"));
                string control = call.Api switch
                {
                    "ACTActionFrameSet" => $" · requested frame {request.ControlValue} (source stores int16)",
                    "ACTActionFrequencySet" => $" · frequency input {request.ControlValue} (engine scale {request.ControlValue!.Value * 60.0 / 64.0:0.###})",
                    _ => " · action clock reset"
                };
                ImGui.BulletText($"{call.Api}({string.Join(", ", call.Arguments)}) → action slot {request.ActionIndex}{control}: {clipItems}");
                ImGui.TextWrapped($"  {Path.GetFileName(call.Source)}:{call.Line} · {call.CallbackPath}");
                if (!string.IsNullOrWhiteSpace(request.IndexBinding))
                    ImGui.TextWrapped($"  action index: {request.IndexBinding}");
                if (!string.IsNullOrWhiteSpace(request.ControlBinding))
                    ImGui.TextWrapped($"  control value: {request.ControlBinding}");
                if (call.BranchConditions.Length > 0)
                {
                    string conditions = string.Join("; ", call.BranchConditions.Select(condition =>
                        condition.Kind == "else" ? $"else of ({condition.Expression})"
                            : $"if ({condition.Expression})"));
                    ImGui.TextWrapped($"  source branch: {conditions}");
                }
            }
        }

        if (analysis.Unresolved.Length > 0
            && ImGui.CollapsingHeader($"Unresolved callback action calls ({analysis.Unresolved.Length})"))
        {
            foreach (var unresolved in analysis.Unresolved)
            {
                var call = unresolved.SourceCall;
                ImGui.BulletText($"{call.Api}({string.Join(", ", call.Arguments)}) · {Path.GetFileName(call.Source)}:{call.Line}");
                ImGui.TextWrapped($"  {call.CallbackPath} · unresolved: {unresolved.Reason}");
            }
        }
    }
}
