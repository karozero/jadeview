namespace JadeView;

internal sealed class CinematicAttackTimeline
{
    private const int GlobalFlagSet = 10518;
    private const int GlobalFlagClear = 10519;
    private const int PlanetAttackFlag = 2718;
    private readonly (float Time, bool Enabled)[] changes;
    private readonly float transitionSpeed;
    private readonly float returnSpeed;

    internal CinematicAttackTimeline(AnimationClip source, WorldData world,
        IEnumerable<CinematicEnvironmentController> controllers)
    {
        var environment = controllers.FirstOrDefault(controller => controller.WorldKey == world.Key);
        transitionSpeed = ValidSpeed(environment?.AttackTransitionSpeed) ? environment!.AttackTransitionSpeed : 1;
        returnSpeed = ValidSpeed(environment?.AttackReturnSpeed) ? environment!.AttackReturnSpeed : 1;
        changes = CinematicLiteralCall.ReadTimeline(source)
            .Where(item => item.Call is { Arguments.Length: 1 }
                && item.Call.Arguments[0] == PlanetAttackFlag
                && item.Call.Selector is GlobalFlagSet or GlobalFlagClear)
            .Select(item => (item.Time, item.Call!.Selector == GlobalFlagSet))
            .OrderBy(change => change.Time)
            .ToArray();
    }

    internal float Evaluate(float time)
    {
        float factor = 0;
        bool enabled = false;
        float previousTime = 0;
        foreach (var change in changes)
        {
            if (change.Time > time) break;
            factor = Advance(factor, enabled, change.Time - previousTime);
            enabled = change.Enabled;
            previousTime = change.Time;
        }
        return Advance(factor, enabled, Math.Max(0, time - previousTime));
    }

    private float Advance(float factor, bool enabled, float elapsed)
    {
        if (!float.IsFinite(elapsed) || elapsed <= 0) return factor;
        float rate = enabled ? transitionSpeed : returnSpeed;
        return Math.Clamp(factor + (enabled ? 1 : -1) * rate * elapsed, 0, 1);
    }

    private static bool ValidSpeed(float? value) => value is { } speed && float.IsFinite(speed) && speed > 0;
}
