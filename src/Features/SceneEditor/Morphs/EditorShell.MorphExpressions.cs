using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawMorphExpressions(SceneActor actor, FaceMorphPreview preview)
    {
        DrawDiscoveredMorphPoses(actor, preview);
        var presets = preview.ExpressionPresets;
        if (presets == null || !ImGui.TreeNode(presets.Name)) return;
        ImGui.TextWrapped("Settled poses from the PC expression-kit table. Each button replaces the current morph pose. Transition timing and dialogue triggers are not replayed; emotion names are unknown.");
        if (presets.Notes != null) ImGui.TextWrapped(presets.Notes);
        foreach (int kit in presets.Kits)
        {
            if (kit != presets.Kits[0]) ImGui.SameLine();
            if (!ImGui.Button($"Kit {kit}")) continue;
            preview.PreviewExpressionKit(kit);
            actor.Evaluate(0);
            RecordMorphProjectEdit(actor, preview);
        }
        ImGui.TreePop();
    }

    private void DrawDiscoveredMorphPoses(SceneActor actor, FaceMorphPreview preview)
    {
        if (!ImGui.TreeNode("Discovered morph poses")) return;
        ImGui.TextWrapped("Automatically found nonzero morph targets in this mesh. Names identify source groups; emotion names and native expression kits are not inferred. Selecting a pose replaces the current weights.");
        ImGui.TextDisabled($"{preview.Expressions.Count} poses found");
        foreach (var expression in preview.Expressions)
        {
            if (!ImGui.Selectable(expression.Name)) continue;
            preview.PreviewExpression(expression.Group);
            actor.Evaluate(0);
            RecordMorphProjectEdit(actor, preview);
        }
        if (preview.Expressions.Count == 0)
            ImGui.TextWrapped("This appearance has no nonzero morph deltas. Check other available morph appearances.");
        ImGui.TreePop();
    }
}
