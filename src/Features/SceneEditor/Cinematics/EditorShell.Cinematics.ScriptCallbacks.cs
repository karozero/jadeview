using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private static void DrawCinematicScriptReferencedObjectCalls(CinematicScriptOwner owner)
    {
        CinematicScriptReferencedObjectCall[] objectCalls =
            CinematicScriptReferencedObjectCallResolver.Resolve(owner);
        if (objectCalls.Length == 0
            || !ImGui.CollapsingHeader($"Source calls using linked objects ({objectCalls.Length})")) return;

        ImGui.TextDisabled("Calls in source-reachable callbacks that use an object reference resolved from this owner's serialized OVA. They show possible scripted operations, not proof of execution or live object state.");
        foreach (var operation in objectCalls.Take(64))
        {
            string target = operation.TargetName ?? $"{operation.ObjectKey:X8}";
            string arguments = string.Join(", ", operation.Arguments);
            string alias = operation.AliasVariable == null
                ? ""
                : $" via {operation.AliasVariable} assigned at line {operation.AssignmentLine}";
            ImGui.BulletText($"{operation.VariableName}{alias} → {target} · {operation.Api}({arguments})");
            ImGui.TextDisabled($"{operation.FunctionName} · {operation.Source}:{operation.Line} · path: {operation.CallbackPath}");
            if (operation.Branches.Length > 0)
                ImGui.TextWrapped($"Source conditions: {string.Join("; ", operation.Branches)}");
        }
        if (objectCalls.Length > 64)
            ImGui.TextDisabled($"Showing 64 of {objectCalls.Length} source calls.");
    }

    private static void DrawCinematicScriptSchedulerEvidence(CinematicScriptOwner owner)
    {
        EditorUi.DrawCategoryTitle("World AI callback scheduling");
        ImGui.TextDisabled($"Serialized GAO flags: 0x{owner.SerializedGaoFlags:X8} · state flags: 0x{owner.SerializedStateFlags:X8}");
        ImGui.TextDisabled("Runtime interpretation follows the retail Iris source. The earlier Jaded engine has different activation gates; use it as architectural context, not as a retail scheduler substitute.");
        foreach (string finding in CinematicScriptSchedulerAnalyzer.Analyze(owner))
            ImGui.TextWrapped(finding);
    }

    private static void DrawCinematicEventSchedulerEvidence(CinematicScriptOwner owner)
    {
        string[] findings = CinematicEventSchedulerAnalyzer.Analyze(owner);
        if (findings.Length == 0) return;

        EditorUi.DrawCategoryTitle("World EVE event dispatch");
        foreach (string finding in findings) ImGui.TextWrapped(finding);
    }

    private void DrawUnmappedCinematicCallbacks(IEnumerable<CinematicScriptOwner> owners)
    {
        CinematicUnmappedCallback[] unmappedCallbacks = CinematicScriptCallbackCoverage.FindUnmapped(owners);
        if (unmappedCallbacks.Length == 0) return;

        var callbackKeys = unmappedCallbacks.GroupBy(callback => callback.Slot.FunctionKey)
            .OrderBy(group => group.Key).ToArray();
        if (!ImGui.CollapsingHeader(
                $"Unmapped callback IDs ({callbackKeys.Length} keys · {unmappedCallbacks.Length} slots)")) return;

        ImGui.TextDisabled("These OVA slots contain configured keys absent from the bundled Iris name table. Their behavior and runtime execution are unknown.");
        foreach (var callbackGroup in callbackKeys.Take(40))
        {
            ImGui.BulletText($"{callbackGroup.Key:X8} · {callbackGroup.Count()} slot(s)");
            string ownerNames = string.Join(", ", callbackGroup.Select(callback => callback.Owner.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(5));
            ImGui.TextWrapped($"  Owners: {ownerNames}");
        }
        if (callbackKeys.Length > 40)
            ImGui.TextDisabled($"Showing 40 of {callbackKeys.Length} distinct callback IDs.");
    }
}
