using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
    private CinematicAudioPreview? _cinematicAudio;
    private uint? _cinematicAudioKey;

    private void ResetCinematicAudio()
    {
        _cinematicAudio?.Dispose();
        _cinematicAudio = null;
        _cinematicAudioKey = null;
    }

    private void EnsureCinematicAudio()
    {
        if (_cinematicPlayback == null || Archive == null) return;
        if (_cinematicAudioKey != _cinematicPlayback.Key)
        {
            ResetCinematicAudio();
            _cinematicAudioKey = _cinematicPlayback.Key;
            if (_cinematicPlayback.AudioCues.Length > 0)
                _cinematicAudio = new(Archive, _cinematicPlayback.AudioCues, _cinematicPlayback.Duration);
        }
    }

    private void DrawCinematicAudioControls()
    {
        DrawCategoryTitle("Audio");
        EnsureCinematicAudio();
        if (_cinematicPlayback == null || Archive == null) return;
        if (_cinematicAudio == null)
        {
            ImGui.TextWrapped("No supported sound-modifier play commands were resolved for this clip.");
            return;
        }
        ImGui.Checkbox("Enable source audio", ref _cinematicAudio.Enabled);
        ImGui.SetNextItemWidth(-1);
        ImGui.SliderFloat("##CinematicVolume", ref _cinematicAudio.Volume, 0, 1, "Volume %.2f");
        ImGui.TextWrapped(_cinematicAudio.Status);
        foreach (string diagnostic in _cinematicAudio.Diagnostics) ImGui.TextWrapped(diagnostic);
        if (ImGui.TreeNode("Source audio cues"))
        {
            foreach (var cue in _cinematicPlayback.AudioCues)
            {
                var entry = Archive.Entries.FirstOrDefault(entry => entry.Key == cue.SoundKey);
                ImGui.TextWrapped($"{cue.Start:F2}s · {entry?.Name ?? cue.SoundKey.ToString("X8")}");
            }
            ImGui.TreePop();
        }
    }
}
