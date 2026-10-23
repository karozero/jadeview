namespace JadeView;

public sealed partial record CinematicAction(int LoopItem, CinematicActionItem[] Items)
{
    internal (AnimationClip Clip, float Time, int ItemIndex)? Sample(float elapsed, int? frequency, float startFrame = 0)
    {
        if (Items.Length == 0) return null;
        foreach (var item in Items) item.Clip.EnsureLoaded();
        float initialRate = Rate(Items[0], frequency);
        if (initialRate <= 0)
            return (Items[0].Clip, Math.Clamp(startFrame / 60f, 0, Items[0].Clip.Duration), 0);
        elapsed += startFrame / 60f / initialRate;
        float totalDuration = 0;
        foreach (var item in Items) totalDuration += Duration(item, frequency);
        if (elapsed >= totalDuration && totalDuration > 0)
        {
            if (LoopItem == byte.MaxValue)
            {
                var last = Items[^1].Clip;
                return (last, last.Duration, Items.Length - 1);
            }
            int loopStart = Math.Clamp(LoopItem, 0, Items.Length - 1);
            float prefixDuration = 0;
            for (int index = 0; index < loopStart; index++)
                prefixDuration += Duration(Items[index], frequency);
            float loopDuration = totalDuration - prefixDuration;
            if (loopDuration > 0) elapsed = prefixDuration + (elapsed - prefixDuration) % loopDuration;
        }

        for (int index = 0; index < Items.Length; index++)
        {
            var item = Items[index];
            float duration = Duration(item, frequency);
            if (elapsed < duration || index == Items.Length - 1)
            {
                float time = elapsed * Rate(item, frequency);
                if (item.Clip.Duration > 0) time %= item.Clip.Duration;
                return (item.Clip, Math.Max(0, time), index);
            }
            elapsed -= duration;
        }
        return null;
    }

    internal bool HasFinished(float elapsed, int? frequency, float startFrame = 0)
    {
        if (LoopItem != byte.MaxValue) return false;
        return elapsed >= CompletionDuration(frequency, startFrame);
    }

    internal float CompletionDuration(int? frequency, float startFrame)
    {
        foreach (var item in Items) item.Clip.EnsureLoaded();
        float rate = Items.Length > 0 ? Rate(Items[0], frequency) : 0;
        float offset = rate > 0 ? startFrame / 60f / rate : 0;
        float total = 0;
        foreach (var item in Items) total += Duration(item, frequency);
        return Math.Max(0, total - offset);
    }

    private static float Duration(CinematicActionItem item, int? frequency)
    {
        float rate = Rate(item, frequency);
        return rate > 0 ? item.Clip.Duration * Math.Max(1, item.Repetitions) / rate : float.PositiveInfinity;
    }

    internal static float Rate(CinematicActionItem item, int? frequency) =>
        frequency.HasValue ? MathF.Floor(frequency.Value * 60f / 64f + .5f) / 60f
            : (60 * item.Frequency / 64) / 60f;
}

public sealed record CinematicActionItem(AnimationClip Clip, uint ShapeKey, int Frequency, int Repetitions)
{
    // ANI_C_AnimFlag_SpeedFromDyna: animation supplies poses, DYN supplies translation.
    internal bool UsesDynamicsTranslation => (Flags & 0x07) == 0x06;
    public byte BlendFrames { get; init; }
    public byte Flags { get; init; }
    public byte CustomBits { get; init; }
    public ushort DesignFlags { get; init; }
    public CinematicActionTransition[] Transitions { get; init; } = [];
}
