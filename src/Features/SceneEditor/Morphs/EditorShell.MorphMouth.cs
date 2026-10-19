using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private float mouthPreviewFactor = FaceMorphMouthCycleSettings.DefaultFactor;
    private float mouthPreviewSpeed = FaceMorphMouthCycleSettings.DefaultSpeed;

    private void DrawMorphMouth(SceneActor actor, SceneObject member, FaceMorphPreview preview)
    {
        var definition = preview.MouthCycleDefinition;
        if (definition == null || actor.FindModule(definition.ModuleSelector) != member) return;
        if (!ImGui.TreeNode("Experimental mouth cycle")) return;
        ImGui.TextWrapped("Manual preview of recovered mouth motion; not synchronized to dialogue. Stop restores the previous pose. Project saving keeps the previous pose. Enable Active experimental mouth cycle in GLB export to include a separate test clip.");
        ImGui.SliderFloat("Mouth strength", ref mouthPreviewFactor, 0, 1, "%.2f");
        ImGui.SliderFloat("Mouth speed", ref mouthPreviewSpeed, .1f, FaceMorphMouthCycleSettings.MaximumSpeed, "%.1f");
        if (ImGui.Button(preview.MouthCyclePlaying ? "Restart mouth cycle" : "Play mouth cycle"))
            preview.StartMouthCycle(mouthPreviewFactor, mouthPreviewSpeed);
        ImGui.SameLine();
        ImGui.BeginDisabled(!preview.MouthCyclePlaying);
        if (ImGui.Button("Stop mouth cycle")) { preview.StopMouthCycle(); actor.Evaluate(0); }
        ImGui.EndDisabled();
        ImGui.TreePop();
    }
}
