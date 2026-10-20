namespace JadeView;

public sealed partial class CinematicBodyTimeline
{
    private void ChangeFrequency(CinematicActionEvent change)
    {
        if (change.Frequency is not (>= 0 and <= 255)) return;
        var segment = segments.LastOrDefault(segment => segment.Start <= change.Time);
        if (segment == null) return;
        segment.FrequencyChanges.Add(new(change.Time - segment.Start, change.Frequency.Value));
        diagnostics.Add($"Source animation frequency at {change.Time:F3}s: {change.Frequency.Value}/64; current frame preserved.");
        if (segment.PendingActionId != null)
            diagnostics.Add($"Frequency change during a pending action bridge at {change.Time:F3}s: destination timing needs reconstruction.");
    }
}
