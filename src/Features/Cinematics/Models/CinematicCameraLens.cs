namespace JadeView;

internal sealed class CinematicCameraLens
{
    // camera_cam_exec_cine / camera_speciale initialization: radians, not millimetres.
    internal const float DefaultAngle = 1.2f;
    internal const float ExportAspect = 4f / 3;
    private sealed record Change(float Time, float From, float To, float Duration);
    private readonly List<Change> changes = [];

    internal CinematicCameraLens(IEnumerable<CinematicCameraEvent> events,
        IEnumerable<(float Time, float Angle)>? scriptViewSetters = null)
    {
        var changesAtSourceTime = events
            .Where(item => item.Mode is 1 or 2)
            .Select((item, order) => (Time: item.Time, Angle: item.FocalAngle,
                Duration: item.Transition, Order: order, IsDirectSet: false))
            .Concat((scriptViewSetters ?? []).Select((item, order) =>
                (item.Time, item.Angle, Duration: 0f, Order: order, IsDirectSet: true)))
            .Where(item => float.IsFinite(item.Time) && float.IsFinite(item.Angle)
                && float.IsFinite(item.Duration) && item.Angle > 0 && item.Angle < MathF.PI)
            .OrderBy(item => item.Time)
            .ThenBy(item => item.IsDirectSet)
            .ThenBy(item => item.Order);

        foreach (var item in changesAtSourceTime)
        {
            changes.Add(new(item.Time, Evaluate(item.Time), item.Angle, Math.Max(0, item.Duration)));
        }
    }

    internal static (float Time, float Angle)[] ReadScriptViewFovChanges(
        IEnumerable<(float Time, CinematicLiteralCall Call)> calls, uint executionTarget) => calls
        .Where(item => item.Call.Target == executionTarget
            && CinematicEngineFunctionCatalog.NameFor(item.Call.Selector) == "VIEWFocaleSet"
            && item.Call.Arguments.Length == 2
            && item.Call.Arguments[0] == 0)
        .Select(item => (item.Time, Angle: BitConverter.UInt32BitsToSingle(item.Call.Arguments[1])))
        .Where(item => float.IsFinite(item.Time) && float.IsFinite(item.Angle)
            && item.Angle > 0 && item.Angle < MathF.PI)
        .OrderBy(item => item.Time)
        .ToArray();

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
