using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawMorphTimeline(SceneActor actor, FaceMorphPreview preview)
    {
        var timeline = preview.Timeline;
        if (timeline.Keys.Count == 0 && !MorphKeyCreationVisible) return;
        if (!ImGui.TreeNode("Saved morph animation")) return;
        ImGui.TextWrapped("User-authored keys. Saved in .bgemod and exported as a separate GLB clip; BF builds do not include them.");
        float time = timeline.Time;
        if (ImGui.InputFloat("Morph time (s)", ref time, .1f, 1, "%.3f") && float.IsFinite(time))
        {
            preview.Seek(Math.Max(0, time));
            actor.Evaluate(0);
            RecordMorphProjectEdit(actor, preview);
        }
        if (MorphKeyCreationVisible)
        {
            if (ImGui.Button("Record / replace morph key"))
            {
                timeline.Record(timeline.Time, preview.CopyWeights());
                RecordMorphProjectEdit(actor, preview);
            }
            ImGui.SameLine();
        }
        if (ImGui.Button("Remove morph key") && timeline.RemoveCurrentKey()) RecordMorphProjectEdit(actor, preview);
        ImGui.BeginDisabled(timeline.Duration <= 0);
        if (ImGui.Button(timeline.Playing ? "Pause morphs" : "Play morphs"))
        {
            if (!timeline.Playing && timeline.Time >= timeline.Duration) preview.Seek(0);
            timeline.Playing = !timeline.Playing;
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        bool loop = timeline.Loop;
        if (ImGui.Checkbox("Loop morphs", ref loop))
        {
            timeline.Loop = loop;
            RecordMorphProjectEdit(actor, preview);
        }
        if (ImGui.Button("Clear morph animation"))
        {
            timeline.Clear();
            RecordMorphProjectEdit(actor, preview);
        }
        ImGui.TextDisabled($"{timeline.Keys.Count} keys, {timeline.Duration:0.###} s; linear interpolation");
        foreach (var key in timeline.Keys.ToArray())
            if (ImGui.Selectable($"Key at {key.Time:0.###} s##MorphKey{key.Time:R}", key.Time == timeline.Time))
            {
                preview.Seek(key.Time);
                actor.Evaluate(0);
                RecordMorphProjectEdit(actor, preview);
            }
        ImGui.TreePop();
    }
}
