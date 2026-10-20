using System.Numerics;

namespace JadeView;

internal sealed class CinematicFlashTimeline
{
    private const int CapabilitySet = 8133;
    private const uint FlashCapability = 0x100;
    private readonly FlashTrack[] tracks;

    internal CinematicFlashTimeline(AnimationClip source, WorldData world,
        IReadOnlyDictionary<uint, CinematicFlashController> controllers)
    {
        var calls = CinematicLiteralCall.ReadTimeline(source)
            .Where(item => item.Call is { Selector: CapabilitySet, Arguments.Length: 2 }).ToArray();
        tracks = controllers.Values.Select(controller =>
        {
            var owner = world.Objects.FirstOrDefault(sceneObject => sceneObject.GaoKey == controller.OwnerKey);
            if (owner == null) return null;
            var snapActor = world.Objects.FirstOrDefault(sceneObject => sceneObject.GaoKey == controller.SnapActorKey) ?? owner;
            var changes = calls.Where(item => item.Call.Target == controller.OwnerKey)
                .Select(item => new CapabilityChange(item.Time,
                    item.Call.Arguments[0] & FlashCapability, item.Call.Arguments[1] & FlashCapability))
                .ToArray();
            return new FlashTrack(controller, snapActor, changes);
        }).OfType<FlashTrack>().ToArray();
    }

    internal CinematicFlashState[] Evaluate(float time)
    {
        var active = new List<CinematicFlashState>();
        foreach (var track in tracks)
        {
            if (!TryGetOpacity(track, time, out float opacity))
            {
                continue;
            }

            active.Add(CreateFlashState(track, opacity));
        }
        return active.ToArray();
    }

    private static bool TryGetOpacity(FlashTrack track, float time, out float opacity)
    {
        bool enabled = (track.Controller.InitialCapabilities & FlashCapability) != 0;
        float? start = enabled ? 0 : null;
        float? stop = null;

        foreach (var change in track.Changes.TakeWhile(change => change.Time <= time))
        {
            bool nextEnabled = change.SetMask != 0 || enabled && change.RemoveMask == 0;
            if (nextEnabled && !enabled)
            {
                start = change.Time;
                stop = null;
            }
            else if (!nextEnabled && enabled)
            {
                stop = change.Time;
            }

            enabled = nextEnabled;
        }

        if (start == null)
        {
            opacity = 0;
            return false;
        }

        opacity = AppearanceOpacity(track.Controller, start.Value, time);
        if (stop is float stopTime)
        {
            opacity *= DisappearanceOpacity(track.Controller, stopTime, time);
        }

        opacity = Math.Clamp(opacity, 0, 1);
        return opacity > 0;
    }

    private static float AppearanceOpacity(CinematicFlashController controller, float start, float time)
    {
        if (time < start)
        {
            return 0;
        }

        if (controller.AppearDelay <= 0 || time >= start + controller.AppearDelay)
        {
            return 1;
        }

        return (time - start) / controller.AppearDelay;
    }

    private static float DisappearanceOpacity(CinematicFlashController controller, float stop, float time)
    {
        if (time < stop)
        {
            return 1;
        }

        if (controller.DisappearDelay <= 0 || time >= stop + controller.DisappearDelay)
        {
            return 0;
        }

        return 1 - (time - stop) / controller.DisappearDelay;
    }

    private static CinematicFlashState CreateFlashState(FlashTrack track, float opacity)
    {
        var controller = track.Controller;
        var sourcePosition = track.SnapActor.RenderMatrix.Translation;
        var actorRotation = Matrix4x4.Decompose(track.SnapActor.RenderMatrix, out _, out var rotation, out _)
            ? Matrix4x4.CreateFromQuaternion(rotation)
            : Matrix4x4.Identity;
        var offset = Vector3.Transform(TransformData.ToRender(controller.Offset), actorRotation);
        float zoom = track.SnapActor.CinematicZoom ?? 1;
        var position = sourcePosition + offset * zoom;

        return new(position, controller.Color, controller.RayCount, opacity,
            controller.BigAlpha,
            Midpoint(controller.BigLengthMin, controller.BigLengthMax),
            Midpoint(controller.BigWidthMin, controller.BigWidthMax),
            controller.SmallAlpha,
            Midpoint(controller.SmallLengthMin, controller.SmallLengthMax),
            Midpoint(controller.SmallWidthMin, controller.SmallWidthMax));
    }

    private static float Midpoint(float first, float second) => Math.Max(0, (first + second) * .5f);
    private sealed record FlashTrack(CinematicFlashController Controller,
        SceneObject SnapActor, CapabilityChange[] Changes);
    private sealed record CapabilityChange(float Time, uint SetMask, uint RemoveMask);
}
