namespace JadeView;

internal static class CinematicObjectKey
{
    internal static bool IsArchiveObject(uint key) => key is > 2 and < uint.MaxValue;
}
