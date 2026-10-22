using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
    private CinematicAudioPreview? _cinematicAudio;
    private uint? _cinematicAudioKey;
    private readonly HashSet<int> _selectedCinematicAudioCueIndices = [];

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
            _selectedCinematicAudioCueIndices.Clear();
            for (int index = 0; index < _cinematicPlayback.AudioCues.Length; index++)
                if (_cinematicPlayback.AudioCues[index].ChoiceIndex == 0)
                    _selectedCinematicAudioCueIndices.Add(index);
        }
        if (_cinematicAudio == null && _selectedCinematicAudioCueIndices.Count > 0)
            _cinematicAudio = new(Archive,
                CinematicAudioCueSelection.Select(_cinematicPlayback.AudioCues, _selectedCinematicAudioCueIndices),
                _cinematicPlayback.Duration);
    }

    private void DrawCinematicAudioControls()
    {
        DrawCategoryTitle("Audio");
        EnsureCinematicAudio();
        if (_cinematicPlayback == null || Archive == null) return;
        if (_cinematicPlayback.AudioCues.Length == 0)
        {
            ImGui.TextWrapped("No source audio cues were resolved for this clip.");
            foreach (string diagnostic in _cinematicPlayback.AudioDiagnostics) ImGui.TextWrapped(diagnostic);
            return;
        }
        DrawCinematicAudioCueSelection();
        if (_cinematicAudio == null)
        {
            ImGui.TextWrapped("Select one or more source audio cues to include in playback.");
            return;
        }
        ImGui.Checkbox("Enable source audio", ref _cinematicAudio.Enabled);
        ImGui.SetNextItemWidth(-1);
        ImGui.SliderFloat("##CinematicVolume", ref _cinematicAudio.Volume, 0, 1, "Volume %.2f");
        ImGui.TextWrapped(_cinematicAudio.Status);
        foreach (string diagnostic in _cinematicAudio.Diagnostics) ImGui.TextWrapped(diagnostic);
        foreach (string diagnostic in _cinematicPlayback.AudioDiagnostics) ImGui.TextWrapped(diagnostic);
    }

    private void DrawCinematicAudioCueSelection()
    {
        var playback = _cinematicPlayback!;
        var archive = Archive!;
        int sourceCueCount = playback.AudioCues.Select((cue, index) => cue.ChoiceGroup ?? $"cue:{index}")
            .Distinct(StringComparer.Ordinal).Count();
        ImGui.TextDisabled($"{sourceCueCount} source cues · {playback.AudioCues.Length} audio banks · "
            + $"{_selectedCinematicAudioCueIndices.Count} selected");
        bool selectionChanged = false;
        if (ImGui.SmallButton("All"))
        {
            var defaultIndices = Enumerable.Range(0, playback.AudioCues.Length)
                .Where(index => playback.AudioCues[index].ChoiceGroup == null
                    || playback.AudioCues[index].ChoiceIndex == 0)
                .ToHashSet();
            selectionChanged = !_selectedCinematicAudioCueIndices.SetEquals(defaultIndices);
            _selectedCinematicAudioCueIndices.Clear();
            _selectedCinematicAudioCueIndices.UnionWith(defaultIndices);
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("None"))
        {
            selectionChanged |= _selectedCinematicAudioCueIndices.Count > 0;
            _selectedCinematicAudioCueIndices.Clear();
        }
        if (ImGui.TreeNode("Choose source audio cues"))
        {
            for (int index = 0; index < playback.AudioCues.Length; index++)
            {
                var cue = playback.AudioCues[index];
                var entry = archive.Entries.FirstOrDefault(entry => entry.Key == cue.SoundKey);
                bool selected = _selectedCinematicAudioCueIndices.Contains(index);
                string label = $"{cue.Start:F2}s · {cue.Label ?? entry?.Name ?? cue.SoundKey.ToString("X8")}"
                    + $" · {cue.OwnerKey:X8}/{cue.ModifierId}";
                if (ImGui.Checkbox($"{label}##CinematicAudioCue{index}", ref selected))
                {
                    selectionChanged = true;
                    CinematicAudioCueSelection.SetSelected(playback.AudioCues,
                        _selectedCinematicAudioCueIndices, index, selected);
                }
            }
            ImGui.TreePop();
        }
        if (!selectionChanged) return;

        _cinematicAudio?.Dispose();
        _cinematicAudio = null;
        EnsureCinematicAudio();
    }
}
