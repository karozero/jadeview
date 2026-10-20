using System.Numerics;
using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
    private void ApplyCinematicCamera(CinematicPlayback playback)
    {
        if (playback.Camera is not { } camera) return;
        var pose = camera.RenderPose(playback.Time);
        if (!Matrix4x4.Decompose(pose, out _, out var rotation, out var position)) return;
        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, rotation);
        Vector3 up = Vector3.Transform(Vector3.UnitY, rotation);
        if (forward.LengthSquared() < 1e-6f || up.LengthSquared() < 1e-6f) return;

        float distance = Math.Clamp(Vector3.Distance(Renderer.Camera.Position, Renderer.Camera.Target), 1, 1000);
        Renderer.Camera.Position = position;
        Renderer.Camera.Target = position + Vector3.Normalize(forward) * distance;
        Renderer.Camera.Up = Vector3.Normalize(up);
        Renderer.CinematicHorizontalFov = camera.Lens?.Evaluate(playback.Time);
        Renderer.Yaw = MathF.Atan2(forward.X, forward.Z);
        Renderer.Pitch = MathF.Asin(Math.Clamp(forward.Y, -.99f, .99f));
    }

    private bool _followCinematicCamera = true;

    private void DrawCinematicCameraControls()
    {
        DrawCategoryTitle("Camera");
        ImGui.Checkbox("Follow source camera", ref _followCinematicCamera);
        var playback = _cinematicPlayback!;
        if (playback.Cameras.Length == 0)
        {
            ImGui.TextDisabled("No source camera track found. Use RMB to explore.");
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
        ImGui.TextWrapped("Disable camera follow to inspect with RMB. Source position/target switches and lens commands follow the timeline. Native transition dynamics and shake are not reconstructed.");
    }
}
