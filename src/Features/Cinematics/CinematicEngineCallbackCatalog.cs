namespace JadeView;

/// <summary>
/// Engine callback IDs named in Jaded AIstruct.h. Iris call sites directly confirm
/// IDs 0-3; IDs 4-9 retain the Jaded label and are marked as corroborating-only.
/// </summary>
internal static class CinematicEngineCallbackCatalog
{
    private static readonly string[] JadedNames =
    [
        "Before display",
        "After display",
        "After record",
        "After blend",
        "When destroyed",
        "Short-way event",
        "Sector activated",
        "Sector deactivated",
        "Sector became visible",
        "Sector became invisible"
    ];

    internal static string Describe(int eventId) => eventId >= 0 && eventId < JadedNames.Length
        ? JadedNames[eventId]
        : $"Unknown engine event {eventId}";

    internal static bool IsConfirmedByIrisCallSite(int eventId) => eventId is >= 0 and <= 3;
}
