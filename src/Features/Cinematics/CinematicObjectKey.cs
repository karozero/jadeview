namespace JadeView;

internal static class CinematicObjectKey
{
    internal static bool IsArchiveObject(uint key) => key is > 2 and < uint.MaxValue;

    internal static string? SpecialActorSlotName(uint key) => key switch
    {
        1 => "AI_MainActor slot 1",
        2 => "AI_MainActor slot 2",
        _ => null
    };
}
