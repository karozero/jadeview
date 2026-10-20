namespace JadeView;

internal sealed class CinematicCameraLens
{
    // camera_cam_exec_cine / camera_speciale initialization: radians, not millimetres.
    internal const float DefaultAngle = 1.2f;
    internal const float ExportAspect = 4f / 3;
    private sealed record Change(float Time, float From, float To, float Duration);
    private readonly List<Change> changes = [];

    internal CinematicCameraLens(IEnumerable<CinematicCameraEvent> events)
    {
        foreach (var item in events.OrderBy(item => item.Time))
        {
            bool valid = item.Mode is 1 or 2 && float.IsFinite(item.Time) && float.IsFinite(item.Transition)
                && item.FocalAngle > 0 && item.FocalAngle < MathF.PI;
            if (!valid) continue; // Zero means retain the preceding lens.
            changes.Add(new(item.Time, Evaluate(item.Time), item.FocalAngle, Math.Max(0, item.Transition)));
        }
    }

    internal float Evaluate(float time)
    {
        var change = changes.LastOrDefault(item => item.Time <= time);
        if (change == null) return DefaultAngle;
        float amount = change.Duration > 0 ? Math.Clamp((time - change.Time) / change.Duration, 0, 1) : 1;
        return change.From + (change.To - change.From) * amount;
    }

    internal (float Time, float Angle)[] Keys => changes.SelectMany(change => new[]
        { (change.Time, Evaluate(change.Time)), (change.Time + change.Duration, Evaluate(change.Time + change.Duration)) })
        .Distinct().OrderBy(key => key.Item1).ToArray();

    internal static float VerticalAngle(float horizontalAngle, float aspect) =>
        2 * MathF.Atan(MathF.Tan(horizontalAngle / 2) / Math.Max(.001f, aspect));
}
