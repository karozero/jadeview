namespace JadeView;

internal sealed class CinematicShockwaveTimeline
{
    private const int ObjectDestroy = 8113;
    private const int ObjectFlagsSet = 8115;
    private const int ObjectInvisibleSet = 8152;
    private const uint VisibilityMask = CinematicObjectState.Invisible | CinematicObjectState.Inactive;
    private readonly Shockwave[] shockwaves;
    private readonly List<ShockwaveState> activeShockwaves = [];

    internal CinematicShockwaveTimeline(AnimationClip source, WorldData world,
        IReadOnlyDictionary<uint, CinematicShockwaveController> controllers)
    {
        var calls = CinematicLiteralCall.ReadTimeline(source);
        shockwaves = controllers.Values.Select(controller =>
        {
            var owner = world.Objects.FirstOrDefault(sceneObject => sceneObject.GaoKey == controller.OwnerKey);
            if (owner == null) return null;
            bool active = (owner.ControlFlags & VisibilityMask) == 0;
            float? start = active ? 0 : null;
            foreach (var item in calls.Where(item => item.Call.Target == controller.OwnerKey))
            {
                bool? next = IsActive(item.Call);
                if (next == null) continue;
                if (next.Value && !active) start = item.Time;
                if (!next.Value) start = null;
                active = next.Value;
            }
            return start is float activation ? new Shockwave(owner, controller.Radius, activation) : null;
        }).OfType<Shockwave>().ToArray();
    }

    internal IReadOnlyList<ShockwaveState> Evaluate(float time)
    {
        activeShockwaves.Clear();
        foreach (var shockwave in shockwaves)
        {
            float elapsed = time - shockwave.Start;
            if (elapsed < 0)
            {
                shockwave.Object.CinematicZoom = 0;
                shockwave.Object.CinematicExcluded = true;
                continue;
            }

            float blend = Math.Clamp(elapsed, 0, 1); // Source randomizes 0.8–1.2; use its neutral rate for repeatable scrubbing.
            float wave = MathF.Sin(blend * MathF.PI * .5f);
            bool finished = elapsed >= 1;
            shockwave.Object.CinematicZoom = finished ? 0 : shockwave.Radius * wave;
            shockwave.Object.CinematicExcluded = finished;
            activeShockwaves.Add(new(shockwave.Object, shockwave.Start, shockwave.Object.CinematicZoom.Value,
                finished ? 0 : wave * wave, finished));
        }
        return activeShockwaves;
    }

    internal void Clear()
    {
        activeShockwaves.Clear();
        foreach (var shockwave in shockwaves)
        {
            shockwave.Object.CinematicZoom = null;
            shockwave.Object.CinematicExcluded = false;
        }
    }

    private static bool? IsActive(CinematicLiteralCall call)
    {
        if (call.Selector == ObjectDestroy && call.Arguments.Length == 0) return false;
        if (call.Selector == ObjectInvisibleSet && call.Arguments.Length == 1) return call.Arguments[0] == 0;
        if (call.Selector != ObjectFlagsSet || call.Arguments.Length != 2) return null;
        uint setMask = call.Arguments[0] & VisibilityMask;
        uint clearMask = call.Arguments[1] & VisibilityMask;
        if ((setMask & VisibilityMask) != 0) return false;
        return clearMask == VisibilityMask ? true : null;
    }

    private sealed record Shockwave(SceneObject Object, float Radius, float Start);

    internal readonly record struct ShockwaveState(SceneObject Object, float StartTime, float Zoom, float Transparency, bool Finished);
}
