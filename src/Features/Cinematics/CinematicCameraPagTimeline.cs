using System.Numerics;

namespace JadeView;

internal sealed class CinematicCameraPagTimeline
{
    private const uint ActiveCapability = 1;
    private const int SetCapabilities = 8133;
    private const int ToggleCapabilities = 8145;
    private const int MaximumModeChanges = 4096;
    private readonly CameraPagTrack[] tracks;

    internal CinematicCameraPagTimeline(IEnumerable<CinematicScriptOwner> owners,
        WorldData world, IEnumerable<(float Time, CinematicLiteralCall Call)> calls)
    {
        var controllers = CinematicCameraPagControllerReader.Read(owners, world);
        var orderedCalls = calls.Where(item => item.Call.Selector is SetCapabilities or ToggleCapabilities)
            .OrderBy(item => item.Time).ToArray();
        tracks = controllers.Select(controller => new CameraPagTrack(controller,
            orderedCalls.Where(item => item.Call.Target == controller.Owner.GaoKey
                && item.Call.Arguments.Length == (item.Call.Selector == SetCapabilities ? 2 : 1))
                .Select(item => new CapabilityChange(item.Time,
                    item.Call.Selector == SetCapabilities ? item.Call.Arguments[0] : 0,
                    item.Call.Selector == SetCapabilities ? item.Call.Arguments[1] : 0,
                    item.Call.Selector == ToggleCapabilities ? item.Call.Arguments[0] : 0))
                .Where(change => float.IsFinite(change.Time))
                .ToArray())).ToArray();
    }

    internal void Evaluate(float time, Matrix4x4? previewCameraPose)
    {
        float requestedTime = float.IsFinite(time) ? Math.Max(0, time) : 0;
        foreach (var track in tracks)
        {
            var state = EvaluateState(track, requestedTime);
            var controller = track.Controller;
            SetFrequency(controller.FirstEffect, state.Enabled ? 50 : 0);
            SetFrequency(controller.SecondEffect, state.Enabled ? 20 : 0);
            if (!state.Enabled) continue;

            SetVisible(controller.FirstEffect);
            SetVisible(controller.SecondEffect);
            if (previewCameraPose is not { } cameraPose) continue;
            ApplyCameraPose(controller.FirstEffect, cameraPose, state.Mode);
            ApplyCameraPose(controller.SecondEffect, cameraPose, state.Mode);
        }
    }

    internal void Clear()
    {
        foreach (var track in tracks)
        {
            if (track.Controller.FirstEffect != null) track.Controller.FirstEffect.PreviewMatrix = null;
            if (track.Controller.SecondEffect != null) track.Controller.SecondEffect.PreviewMatrix = null;
        }
    }

    private static CallbackState EvaluateState(CameraPagTrack track, float requestedTime)
    {
        uint capabilities = track.Controller.Owner.InitialCapabilities & ushort.MaxValue;
        bool enabled = HasCapability(capabilities);
        int mode = track.Controller.InitialMode;
        float timerStart = track.Controller.InitialTimer;
        float delay = track.Controller.InitialDelay;

        foreach (var change in track.Changes)
        {
            if (change.Time > requestedTime) break;
            AdvanceMode(ref mode, ref timerStart, ref delay, enabled, change.Time);
            capabilities = ApplyChange(capabilities, change);
            bool nextEnabled = HasCapability(capabilities);
            if (!enabled && nextEnabled && change.Time - timerStart >= delay)
            {
                ToggleMode(ref mode, ref timerStart, ref delay, change.Time);
            }
            enabled = nextEnabled;
        }

        AdvanceMode(ref mode, ref timerStart, ref delay, enabled, requestedTime);
        return new(enabled, mode);
    }

    private static void AdvanceMode(ref int mode, ref float timerStart, ref float delay,
        bool enabled, float until)
    {
        int changes = 0;
        while (enabled && until - timerStart >= delay && changes++ < MaximumModeChanges)
            ToggleMode(ref mode, ref timerStart, ref delay, timerStart + delay);
    }

    private static void ToggleMode(ref int mode, ref float timerStart, ref float delay, float changedAt)
    {
        mode ^= 1;
        timerStart = changedAt;
        delay = 2.5f;
    }

    private static uint ApplyChange(uint capabilities, CapabilityChange change) =>
        ((capabilities | (change.SetMask & ushort.MaxValue))
        & ~(change.ClearMask & ushort.MaxValue)) ^ (change.ToggleMask & ushort.MaxValue);

    private static bool HasCapability(uint capabilities) => (capabilities & ActiveCapability) != 0;

    private static void SetFrequency(SceneObject? effect, float frequency)
    {
        if (effect?.CinematicParticles is { Values.Length: > 4 } particles)
            particles.Values[4] = frequency;
    }

    private static void SetVisible(SceneObject? effect)
    {
        if (effect == null) return;
        uint flags = effect.PreviewControlFlags ?? effect.ControlFlags;
        effect.PreviewControlFlags = flags & ~CinematicObjectState.Invisible;
        effect.CinematicExcluded = false;
    }

    private static void ApplyCameraPose(SceneObject? effect, Matrix4x4 cameraPose, int mode)
    {
        if (effect == null) return;
        Vector3 position = new(cameraPose.M41, cameraPose.M42, cameraPose.M43);
        Vector3 horizon = AxisX(cameraPose);
        Vector3 sight = -AxisY(cameraPose);
        Vector3 banking = AxisZ(cameraPose);
        float side = (mode & 1) != 0 ? 6 : -6;
        Vector3 effectPosition = position + sight * 5 + horizon * side + banking * 2;
        Vector3 effectSight = (mode & 1) != 0 ? -horizon : horizon;

        if (!TryBuildOrientation(effectSight, banking, out var orientation)
            || !Matrix4x4.Decompose(effect.PreviewMatrix ?? AnimationMath.Trs(effect.Transform),
                out var scale, out _, out _))
            return;

        Matrix4x4 pose = Matrix4x4.CreateScale(scale) * orientation;
        pose.M41 = effectPosition.X;
        pose.M42 = effectPosition.Y;
        pose.M43 = effectPosition.Z;
        effect.PreviewMatrix = pose;
    }

    private static Vector3 AxisX(Matrix4x4 matrix) => Vector3.Normalize(new(matrix.M11, matrix.M12, matrix.M13));
    private static Vector3 AxisY(Matrix4x4 matrix) => Vector3.Normalize(new(matrix.M21, matrix.M22, matrix.M23));
    private static Vector3 AxisZ(Matrix4x4 matrix) => Vector3.Normalize(new(matrix.M31, matrix.M32, matrix.M33));

    private static bool TryBuildOrientation(Vector3 sight, Vector3 banking, out Matrix4x4 orientation)
    {
        orientation = Matrix4x4.Identity;
        if (!IsUsable(sight) || !IsUsable(banking)) return false;
        Vector3 yAxis = Vector3.Normalize(-sight);
        Vector3 zAxis = Vector3.Normalize(banking);
        Vector3 xAxis = Vector3.Cross(yAxis, zAxis);
        if (!IsUsable(xAxis)) return false;
        xAxis = Vector3.Normalize(xAxis);
        yAxis = Vector3.Normalize(Vector3.Cross(zAxis, xAxis));
        orientation = new(
            xAxis.X, xAxis.Y, xAxis.Z, 0,
            yAxis.X, yAxis.Y, yAxis.Z, 0,
            zAxis.X, zAxis.Y, zAxis.Z, 0,
            0, 0, 0, 1);
        return true;
    }

    private static bool IsUsable(Vector3 vector) => float.IsFinite(vector.LengthSquared())
        && vector.LengthSquared() > 1e-8f;

    private sealed record CameraPagTrack(CinematicCameraPagController Controller,
        CapabilityChange[] Changes);
    private readonly record struct CapabilityChange(float Time, uint SetMask,
        uint ClearMask, uint ToggleMask);
    private readonly record struct CallbackState(bool Enabled, int Mode);
}
