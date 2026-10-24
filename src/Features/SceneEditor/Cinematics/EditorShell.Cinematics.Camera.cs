using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
    private SceneRenderer.ViewportCameraState? _cinematicViewportCamera;
    private bool _cinematicCameraWasFollowing;

    private void ApplyCinematicCamera(CinematicPlayback playback)
    {
        if (playback.Camera is not { } camera) return;
        var pose = camera.RenderPose(playback.Time);
        Renderer.TrySetCameraPose(pose, camera.Lens?.Evaluate(playback.Time));
    }

    private bool _followCinematicCamera = true;

    private void DrawCinematicCameraControls()
    {
        DrawCategoryTitle("Camera");
        ImGui.Checkbox("Follow source camera", ref _followCinematicCamera);
        var playback = _cinematicPlayback!;
        ImGui.BeginDisabled(_followCinematicCamera || _cinematicViewportCamera is null);
        if (ImGui.Button("Reset viewport view", new System.Numerics.Vector2(-1, 0))
            && _cinematicViewportCamera is { } stockCamera)
            Renderer.RestoreViewportCameraState(stockCamera);
        ImGui.EndDisabled();
        if (playback.Cameras.Length == 0)
        {
            ImGui.TextDisabled("No source camera track found. RMB controls the viewport camera.");
            return;
        }
        int cameraIndex = playback.CameraIndex;
        string[] names = playback.Cameras.Select(binding => binding.FromSourceEvent
            ? $"{binding.Source.Name} · {binding.ReceiverKey:X8}" : binding.Source.Name).ToArray();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##CinematicCamera", ref cameraIndex, names, names.Length))
            playback.CameraIndex = cameraIndex;
        ImGui.TextDisabled(playback.Camera?.FromSourceEvent == true
            ? "Camera routing: source message 60" : "Camera/target pair: helper-name fallback");
        ImGui.TextWrapped("Disable follow to control the viewport camera: hold RMB to look around from its current position; use WASD/QE to move, Shift to move faster and the wheel to change speed. Reset viewport view restores the scene framing captured when this cinematic loaded and stays in manual control. Enable follow to return to the source camera. Native transition dynamics and shake are not reconstructed.");
    }
}
