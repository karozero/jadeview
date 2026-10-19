using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawMorphAppearances(SceneActor actor, SceneObject member)
    {
        var models = actor.SourceAppearances(member);
        if (models.Count == 0) return;
        if (!ImGui.BeginCombo("Morph appearance", $"Current geometry {member.RenderGeometryKey:X8}")) return;
        if (ImGui.Selectable("Use clip appearance")) actor.RestoreClipAppearance(member);
        for (int index = 0; index < models.Count; index++)
        {
            var model = models[index];
            if (!Resolver.Geometries.TryGetValue(model.RenderGeometryKey, out var geometry)) continue;
            var preview = FaceMorphPreview.Get(model, geometry);
            if (preview?.Data == null) continue;
            string label = $"{index}: {model.Name} · {preview.Expressions.Count} poses##MorphAppearance{index}";
            if (ImGui.Selectable(label)) actor.PreviewAppearance(member, index);
        }
        ImGui.EndCombo();
    }
}
