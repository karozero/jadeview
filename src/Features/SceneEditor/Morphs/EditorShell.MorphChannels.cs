using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawMorphChannels(SceneActor actor, FaceMorphPreview preview)
    {
        if (preview.ChannelCount == 0 || !ImGui.TreeNode("PC morph channels")) return;
        ImGui.TextWrapped("Explicit PC channel preview. Progression moves through ordered group references; it is not a group number. Changes replace the current target weights; the resulting pose exports to GLB.");
        ImGui.TextDisabled(preview.ChannelsActive ? "Channel controls active" : "Direct target weights active");
        for (int index = 0; index < preview.ChannelCount; index++)
        {
            ImGui.PushID(index);
            var state = preview.GetChannel(index);
            var references = preview.Data!.Channels[index].GroupReferences;
            ImGui.Text($"Channel {index}: {references.Length} reference slots");
            float weight = state.Weight, progression = state.Progression;
            bool weightChanged = ImGui.SliderFloat("Weight", ref weight, -1, 1, "%.3f");
            bool progressionChanged = ImGui.SliderFloat("Progression", ref progression, 0, Math.Max(1, references.Length), "%.3f");
            if (weightChanged || progressionChanged)
            {
                preview.SetChannel(index, weight, progression);
                actor.Evaluate(0);
                RecordMorphProjectEdit(actor, preview);
            }
            ImGui.PopID();
        }
        ImGui.TreePop();
    }
}
