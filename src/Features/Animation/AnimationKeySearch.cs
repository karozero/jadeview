namespace JadeView;

internal static class AnimationKeySearch
{
    // AnimationReader appends keys in serialized frame order, so timelines are sorted.
    // Return the last key at or before time, including the last of duplicate timestamps.
    internal static int LastAtOrBefore(IReadOnlyList<AnimationKey> keys, float time)
    {
        int low = 0;
        int high = keys.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (keys[middle].Time <= time) low = middle + 1;
            else high = middle;
        }
        return low - 1;
    }
}
