namespace JadeView;

internal static class CinematicEventSchedulerAnalyzer
{
    private const uint EventTableFlag = 0x02000000;
    private const uint PendingInactiveFlag = 0x00400000;
    private const uint DistanceCullExtraFlag = 0x4;

    internal static string[] Analyze(CinematicScriptOwner owner)
    {
        bool hasEventList = HasResourceKey(owner.EventsKey) || HasResourceKey(owner.SceneEventsKey);
        if (!hasEventList) return [];

        bool eventTableFlagSet = (owner.SerializedGaoFlags & EventTableFlag) != 0;
        string flagFinding = eventTableFlagSet
            ? $"Serialized GAO flag 0x{EventTableFlag:X8} is set; when world activation succeeds, Iris copies this object into eventGameObjectsTablePF while building the EOT tables."
            : $"Serialized GAO flag 0x{EventTableFlag:X8} is clear; the serialized snapshot does not request eventGameObjectsTablePF membership unless runtime code changes the flag before the EOT build.";

        return
        [
            $"Linked event resources: Events {FormatKey(owner.EventsKey)}, SceneEvents {FormatKey(owner.SceneEventsKey)}.",
            flagFinding,
            $"EVE_MainCall advances the attached list only for a valid world-table entry with an EVE component, runtime flags2 0x{PendingInactiveFlag:X8} clear, and nonzero distance when extraFlag 0x{DistanceCullExtraFlag:X} is set. World activation, table membership, distance, and actual track execution are not proven by this BF snapshot."
        ];
    }

    private static bool HasResourceKey(uint key) => key is > 2 and < uint.MaxValue;

    private static string FormatKey(uint key) => HasResourceKey(key) ? $"{key:X8}" : "none";
}
