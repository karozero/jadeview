using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private SceneObject? morphPreviewTarget;
    private bool showEmptyMorphGroups;

    private void DrawMorphControls()
    {
        var actor = SelectedObject?.Actor;
        if (actor == null) return;
        var candidates = actor.Members.Where(member => !member.PreviewHidden &&
            actor.HasMorphAppearance(member)).ToArray();
        if (candidates.Length == 0 || !ImGui.CollapsingHeader("Facial / vertex morphs")) return;
        if (!candidates.Contains(morphPreviewTarget))
            morphPreviewTarget = candidates.OrderByDescending(member => GetMorphPreview(member)?.Data?.Groups.Length ?? 0).First();
        if (ImGui.BeginCombo("Morph mesh", morphPreviewTarget!.Name))
        {
            foreach (var candidate in candidates)
                if (ImGui.Selectable($"{candidate.Name}##{candidate.Id}", candidate == morphPreviewTarget))
                    morphPreviewTarget = candidate;
            ImGui.EndCombo();
        }
        actor.ResolveMorphAppearance(morphPreviewTarget, CountMorphPoses);
        DrawMorphAppearances(actor, morphPreviewTarget);
        var preview = GetMorphPreview(morphPreviewTarget);
        if (preview?.Data == null)
        {
            ImGui.TextWrapped(preview?.Error ?? "No unambiguous morph payload for the selected mesh.");
            return;
        }
        ImGui.TextWrapped("User-authored morph preview. Original facial timing is not reconstructed. Controls and keys are saved in .bgemod; evaluated weights export to GLB. BF builds do not include them.");
        if (preview.Expressions.Count == 0)
            ImGui.TextWrapped("This appearance has no nonzero morph targets. Select another Morph appearance if available.");
        if (ImGui.Button("Reset morph weights"))
        {
            preview.Reset();
            actor.Evaluate(0);
            RecordMorphProjectEdit(actor, preview);
        }
        ImGui.Checkbox("Show empty groups", ref showEmptyMorphGroups);
        DrawMorphChannels(actor, preview);
        DrawMorphBlink(actor, preview);
        DrawMorphMouth(actor, morphPreviewTarget, preview);
        DrawMorphExpressions(actor, preview);
        DrawMorphWeights(actor, preview);
        DrawMorphTimeline(actor, preview);
    }

    private FaceMorphPreview? GetMorphPreview(SceneObject sceneObject) =>
        Resolver.Geometries.TryGetValue(sceneObject.RenderGeometryKey, out var geometry)
            ? FaceMorphPreview.Get(sceneObject, geometry) : null;

    private int CountMorphPoses(SceneObject member) => GetMorphPreview(member)?.Expressions.Count ?? 0;

    private void DrawMorphWeights(SceneActor actor, FaceMorphPreview preview)
    {
        foreach (var group in preview.Data!.Groups)
        {
            bool empty = group.VertexIndices.Length == 0;
            if (empty && !showEmptyMorphGroups) continue;
            ImGui.BeginDisabled(empty);
            var issue = preview.SourceIssues.FirstOrDefault(issue => issue.Group == group.Index);
            float weight = preview.GetWeight(group.Index);
            string label = $"PC_Morph_{group.Index:D2}" + (issue != null ? " [!]" : "");
            if (ImGui.SliderFloat(label, ref weight, 0, 1, "%.3f"))
            {
                preview.SetWeight(group.Index, weight);
                actor.Evaluate(0);
                RecordMorphProjectEdit(actor, preview);
            }
            ImGui.EndDisabled();
            if (issue != null && ImGui.IsItemHovered()) ImGui.SetTooltip(issue.Message);
            if (issue != null && weight != 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Yellow);
                ImGui.TextWrapped(issue.Message);
                ImGui.PopStyleColor();
            }
        }
    }
}
