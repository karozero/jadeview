namespace JadeView;

public sealed partial class CinematicBodyTimeline
{
    private readonly List<float> gameplayHandoffTimes = [];

    internal float[] ActionModeResetTimes => segments
        .Select(segment => segment.Start)
        .Distinct()
        .Order()
        .ToArray();

    internal float[] GameplayHandoffTimes => gameplayHandoffTimes.ToArray();

    private void RecordGameplayHandoff(float time)
    {
        if (float.IsFinite(time) && time >= 0)
            gameplayHandoffTimes.Add(time);
    }
}
