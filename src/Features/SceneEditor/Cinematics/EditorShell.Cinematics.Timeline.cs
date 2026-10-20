using System.Numerics;
using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawCinematicInspector()
    {
        DrawPanelTitle("CINEMATIC TIMELINE", "Select a shared source clip to evaluate matching actors and camera at one timestamp.");
        if (_cinematicWorld == null)
        {
            ImGui.TextDisabled("Choose a cinematic from the Cutscenes browser below.");
            return;
        }

        DrawCategoryTitle("Source");
        ImGui.TextWrapped($"{_cinematicWorld.Name}\n{_cinematicWorld.Actors.Count} actor rigs · {_cinematicWorld.Objects.Count} objects");
        DrawCopyNameContextMenu("CinematicSourceName", _cinematicWorld.Name);
        DrawCategoryTitle("Shared animation tracks");
        if (_cinematicPlayers.Length == 0)
        {
            ImGui.TextWrapped("No shared cinematic animation clip was linked to multiple loaded actor rigs. This scene needs further track/owner decoding.");
            return;
        }

        SelectCinematicClip();
        DrawPlaybackControls();
        DrawCinematicAudioControls();
        DrawCategoryTitle("Scene state");
        ImGui.Checkbox("Show hidden / conditional objects (debug)", ref Renderer.ShowGameHidden);
        DrawCinematicCameraControls();
        DrawParticipants();
    }

    private void SelectCinematicClip()
    {
        int selectedIndex = Array.IndexOf(_cinematicPlayers, _cinematicPlayback);
        if (selectedIndex < 0) selectedIndex = 0;
        string[] clipNames = _cinematicPlayers.Select(player =>
            $"{player.Name} · {player.ParticipantCount} tracks · {player.Duration:F1}s").ToArray();
        ImGui.SetNextItemWidth(-1);
        bool changed = ImGui.Combo("##CinematicClip", ref selectedIndex, clipNames, clipNames.Length);
        DrawCopyNameContextMenu("CinematicClipName", _cinematicPlayers[selectedIndex].Name);
        if (changed || _cinematicPlayback == null)
        {
            _cinematicPlayback?.Deactivate();
            ResetCinematicAudio();
            _cinematicPlayback = _cinematicPlayers[selectedIndex];
            _cinematicPlayback.Activate();
        }
        EnsureCinematicAudio();
    }

    private void DrawPlaybackControls()
    {
        DrawCategoryTitle("Playback");
        bool waitingForAudio = _cinematicAudio is { Enabled: true, IsLoading: true };
        ImGui.BeginDisabled(waitingForAudio && !_cinematicPlayback!.Playing);
        if (ImGui.Button(_cinematicPlayback!.Playing ? "Pause" : "Play"))
        {
            if (_cinematicPlayback.Playing) _cinematicPlayback.Pause();
            else _cinematicPlayback.Play();
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Restart")) _cinematicPlayback.Restart();
        ImGui.SameLine();
        if (ImGui.Button("Export synchronized actors")) OpenCinematicExport();

        float playhead = _cinematicPlayback.Time;
        ImGui.SetNextItemWidth(-1);
        bool changed = ImGui.SliderFloat("##CinematicTime", ref playhead, 0,
            Math.Max(_cinematicPlayback.Duration, .001f),
            $"{_cinematicPlayback.Time:F2} / {_cinematicPlayback.Duration:F2} s");
        if (changed)
        {
            _cinematicPlayback.Pause();
            _cinematicPlayback.Seek(playhead);
        }
    }

    private void OpenCinematicExport()
    {
        _exportTarget = null;
        _exportOptions.Animations = true;
        _exportOptions.AllClips = false;
        _exportOptions.ExperimentalMouthCycle = false;
        _openExport = true;
    }

    private void DrawParticipants()
    {
        DrawCategoryTitle("Participants");
        int participantIndex = 0;
        foreach (string participant in _cinematicPlayback!.ParticipantNames)
        {
            ImGui.BulletText(participant);
            DrawCopyNameContextMenu($"CinematicParticipantName##{participantIndex++}", participant);
        }
        DrawCategoryTitle("Body animation bindings");
        foreach (string diagnostic in _cinematicPlayback.BodyDiagnostics)
            ImGui.TextWrapped(diagnostic);
        DrawCategoryTitle("Facial animation bindings");
        foreach (string diagnostic in _cinematicPlayback.FacialDiagnostics)
            ImGui.TextWrapped(diagnostic);
        ImGui.TextWrapped("Actors, source camera, supported sound modifiers and facial controls follow one source clock. Mouth forms use authored controls; phoneme-level lip sync and the full script VM are not reconstructed.");
    }

    private void UpdateCinematicPlayback()
    {
        Renderer.CinematicShutter = null;
        Renderer.CinematicFlashes = [];
        Renderer.CinematicLightnings = [];
        Renderer.CinematicScreenFade = null;
        Renderer.CinematicHorizontalFov = null;
        Renderer.CinematicMaterials = null;
        if (!_cutsceneMode || _cinematicPlayback == null) return;
        _cinematicPlayback.Advance(Raylib_cs.Raylib.GetFrameTime());
        Renderer.CinematicShutter = _cinematicPlayback.Shutter;
        Renderer.CinematicFlashes = _cinematicPlayback.Flashes;
        Renderer.CinematicLightnings = _cinematicPlayback.Lightnings;
        Renderer.CinematicScreenFade = _cinematicPlayback.ScreenFade;
        Renderer.CinematicMaterials = _cinematicPlayback.MaterialState;
        _cinematicAudio?.Update(_cinematicPlayback.Time, _cinematicPlayback.Playing);
        if (_cinematicPlayback.Playing && _soundPlaying)
        {
            Raylib_cs.Raylib.StopSound(_sound);
            _soundPlaying = false;
        }
        if (Renderer.IsFlying) _followCinematicCamera = false;
        if (_followCinematicCamera && !Renderer.IsFlying) ApplyCinematicCamera(_cinematicPlayback);
    }

}
