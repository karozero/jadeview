using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
    private CinematicAudioPreview? _cinematicAudio;
    private (uint WorldKey, uint ClipKey)? _cinematicAudioIdentity;
    private float _cinematicAudioVolume = .8f;
    private readonly HashSet<int> _selectedCinematicAudioCueIndices = [];

    private void ResetCinematicAudio()
    {
        if (_cinematicAudio != null)
            _cinematicAudioVolume = _cinematicAudio.Volume;
        _cinematicAudio?.Dispose();
        _cinematicAudio = null;
        _cinematicAudioIdentity = null;
    }

    private void EnsureCinematicAudio()
    {
        if (_cinematicPlayback == null || Archive == null) return;
        var requestedIdentity = (_cinematicPlayback.WorldKey, _cinematicPlayback.Key);
        if (_cinematicAudioIdentity != requestedIdentity)
        {
            ResetCinematicAudio();
            _cinematicAudioIdentity = requestedIdentity;
            _selectedCinematicAudioCueIndices.Clear();
            _selectedCinematicAudioCueIndices.UnionWith(
                CinematicAudioCueSelection.CreateDefaultIndices(_cinematicPlayback.AudioCues));
        }
        if (_cinematicAudio == null && _selectedCinematicAudioCueIndices.Count > 0)
        {
            _cinematicAudio = new(Archive,
                CinematicAudioCueSelection.Select(_cinematicPlayback.AudioCues, _selectedCinematicAudioCueIndices),
                _cinematicPlayback.Duration);
            _cinematicAudio.Volume = _cinematicAudioVolume;
        }
    }

    private void DrawCinematicAudioControls()
    {
        DrawCategoryTitle("Audio");
        EnsureCinematicAudio();
        if (_cinematicPlayback == null || Archive == null) return;
        if (_cinematicPlayback.AudioCues.Length == 0)
        {
            ImGui.TextWrapped("No source audio cues were resolved for this clip.");
            DrawCinematicAudioDiagnostics("Audio diagnostics", _cinematicPlayback.AudioDiagnostics);
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
        if (ImGui.SliderFloat("##CinematicVolume", ref _cinematicAudio.Volume, 0, 1, "Volume %.2f"))
            _cinematicAudioVolume = _cinematicAudio.Volume;
        ImGui.TextWrapped(_cinematicAudio.Status);
        DrawCinematicAudioDiagnostics("Playback diagnostics", _cinematicAudio.Diagnostics);
        DrawCinematicAudioDiagnostics("Source diagnostics", _cinematicPlayback.AudioDiagnostics);
    }

    private static void DrawCinematicAudioDiagnostics(string title, IReadOnlyList<string> diagnostics)
    {
        if (diagnostics.Count == 0) return;
        if (!ImGui.TreeNode($"{title} ({diagnostics.Count})")) return;
        foreach (string diagnostic in diagnostics) ImGui.TextWrapped(diagnostic);
        ImGui.TreePop();
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
            var defaultIndices = CinematicAudioCueSelection.CreateDefaultIndices(playback.AudioCues);
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
