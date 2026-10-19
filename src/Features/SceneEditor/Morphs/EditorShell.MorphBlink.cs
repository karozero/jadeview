using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private string? morphBlinkError;
    private void DrawMorphBlink(SceneActor actor, FaceMorphPreview preview)
    {
        if (!preview.SupportsPcBlink || !ImGui.TreeNode(preview.BlinkTitle)) return;
        ImGui.TextWrapped("One manually triggered cycle using the recovered PC envelope (about 0.21 s). Keeps the current eyelid baseline and other morphs. Game trigger scheduling and speech are not replayed.");
        if (ImGui.Button(preview.BlinkPlaying ? "Restart blink" : "Preview blink")) preview.StartBlink();
        ImGui.SameLine();
        ImGui.BeginDisabled(!preview.BlinkPlaying);
        if (ImGui.Button("Stop blink")) { preview.StopBlink(); actor.Evaluate(0); }
        ImGui.EndDisabled();
        if (MorphKeyCreationVisible && preview.CanBakeBlink) DrawMorphBlinkBake(actor, preview);
        ImGui.TreePop();
    }

    private void DrawMorphBlinkBake(SceneActor actor, FaceMorphPreview preview)
    {
        bool overlaps = preview.Timeline.Keys.Any(key => key.Time >= preview.Timeline.Time
            && key.Time <= preview.Timeline.Time + FaceMorphBlinkCurve.Duration);
        ImGui.BeginDisabled(overlaps);
        if (ImGui.Button("Bake blink at morph cursor"))
        {
            try
            {
                preview.BakeBlink();
                actor.Evaluate(0);
                RecordMorphProjectEdit(actor, preview);
                morphBlinkError = null;
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            { morphBlinkError = error.Message; }
        }
        ImGui.EndDisabled();
        if (overlaps) ImGui.TextWrapped("Baking needs an empty interval at the morph cursor; existing keys are preserved.");
        if (morphBlinkError != null) ImGui.TextWrapped(morphBlinkError);
    }
}
