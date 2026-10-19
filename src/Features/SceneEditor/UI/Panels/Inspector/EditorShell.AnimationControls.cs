using static JadeView.EditorUi;
using System.Numerics;
using ImGuiNET;

namespace JadeView;
public sealed partial class EditorShell
{
    private void DrawAnimationControls()
    {
        var actor = SelectedObject?.Actor;
        if (actor == null)
            return;
        DrawCategoryTitle("Animation clip", "Choose a decoded skeletal clip, or Default pose to restore the base appearance.");
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, EditorTheme.CompactFramePadding * _scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, EditorTheme.CompactItemSpacing * _scale);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##ActorClip", actor.Clip?.Name ?? "Default pose"))
        {
            bool defaultSelected = actor.Clip == null;
            if (ImGui.Selectable("Default pose", defaultSelected))
                actor.SelectClip(null);
            if (defaultSelected)
                ImGui.SetItemDefaultFocus();
            for (int clipIndex = 0; clipIndex < actor.Clips.Count; clipIndex++)
            {
                var clip = actor.Clips[clipIndex];
                bool selected = actor.Clip == clip;
                if (ImGui.Selectable($"{clipIndex:D3} · {clip.Name}", selected))
                    actor.SelectClip(clip);
                if (selected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        if (actor.Diagnostic != null)
            ImGui.TextWrapped(actor.Diagnostic);
        else if (actor.Clips.Count == 0)
            ImGui.TextDisabled("No animations found in loaded packages.");
        if (actor.Clip is { } activeClip)
        {
            DrawCategoryTitle("Playback");
            if (ImGui.Button(actor.Playing ? "Pause" : "Play"))
            {
                if (!actor.Playing && actor.Time >= activeClip.Duration)
                    actor.Time = 0;
                actor.Playing = !actor.Playing;
            }

            ImGui.SameLine();
            if (ImGui.Button("Restart"))
            {
                actor.Time = 0;
                actor.Evaluate(0);
            }

            ImGui.SameLine();
            ImGui.Checkbox("Loop", ref actor.Loop);
            ImGui.Checkbox("Root motion", ref actor.RootMotion);
            float time = actor.Time;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.SliderFloat("##ClipTime", ref time, 0, Math.Max(activeClip.Duration, .001f), "%.2f s"))
            {
                actor.Time = time;
                actor.Playing = false;
                actor.Evaluate(0);
            }

            ImGui.SetNextItemWidth(100);
            ImGui.SliderFloat("Speed", ref actor.Speed, .1f, 3, "%.2fx");
            if (activeClip.Error != null)
                ImGui.TextColored(Yellow, activeClip.Error);
            else if (activeClip.Unsupported > 0)
                ImGui.TextColored(Yellow, $"Unsupported events: {activeClip.Unsupported}");
        }

        ImGui.Separator();
        ImGui.PopStyleVar(2);
    }
}
