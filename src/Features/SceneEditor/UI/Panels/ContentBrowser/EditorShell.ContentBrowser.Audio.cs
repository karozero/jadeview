using static JadeView.EditorUi;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using Forms = System.Windows.Forms;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawAudioTab()
    {
        if (_catalogPreview?.Audio is not { } audio)
        {
            ImGui.TextDisabled("Select an audio file (.wad, .waa, .wam, .wac) in the BF archive.");
            if (Archive != null && ImGui.Button("Find first audio file"))
            {
                var soundEntry = Archive.Entries.FirstOrDefault(catalogEntry => catalogEntry.Name.EndsWith(".waa", StringComparison.OrdinalIgnoreCase) || catalogEntry.Name.EndsWith(".wad", StringComparison.OrdinalIgnoreCase));
                if (soundEntry != null) SelectCatalogEntry(soundEntry);
            }
            return;
        }

        DrawCategoryTitle("Audio resource");
        ImGui.TextUnformatted(_selectedCatalogEntry?.Name ?? "Audio");
        ImGui.TextDisabled($"Duration: {audio.Seconds:F1} s · Sample rate: {audio.Rate} Hz · Channels: {audio.Channels} · Format: PCM 16-bit");

        DrawCategoryTitle("Waveform");
        if (_waveform.Length > 0)
        {
            ImGui.PlotHistogram("##Waveform", ref _waveform[0], _waveform.Length, 0, "", 0, 1, new Vector2(-1, 80 * _scale));
        }

        DrawCategoryTitle("Playback");
        ImGui.BeginDisabled(!_soundLoaded);
        if (ImGui.Button(_soundPlaying ? "Pause" : "Play"))
        {
            if (_soundPlaying)
            {
                Raylib.PauseSound(_sound);
                _soundPlaying = false;
            }
            else
            {
                Raylib.PlaySound(_sound);
                _soundPlaying = true;
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Stop"))
        {
            Raylib.StopSound(_sound);
            _soundPlaying = false;
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.SetNextItemWidth(120 * _scale);
        if (ImGui.SliderFloat("Volume", ref _volume, 0, 1) && _soundLoaded)
        {
            Raylib.SetSoundVolume(_sound, _volume);
        }

        DrawCategoryTitle("Export");
        if (ImGui.Button("Export WAV"))
        {
            using var dialog = new Forms.SaveFileDialog
            {
                Filter = "WAVE Audio (*.wav)|*.wav",
                FileName = Path.GetFileNameWithoutExtension(_selectedCatalogEntry?.Name ?? "sound") + ".wav"
            };
            if (dialog.ShowDialog() == Forms.DialogResult.OK)
            {
                File.WriteAllBytes(dialog.FileName, audio.Wav);
                _statusMessage = $"Audio exported: {dialog.FileName}";
            }
        }
    }
}
