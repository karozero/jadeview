namespace JadeView;

internal static class CinematicEventSchedulerAnalyzer
{
    private const uint EventTableFlag = 0x02000000;
    private const uint PendingInactiveFlag = 0x00400000;
    private const uint DistanceCullExtraFlag = 0x4;

    internal static string[] Analyze(CinematicScriptOwner owner)
    {
        if (!HasResourceKey(owner.SceneEventsKey)) return [];

        bool eventTableFlagSet = (owner.SerializedGaoFlags & EventTableFlag) != 0;
        string flagFinding = eventTableFlagSet
            ? $"Serialized GAO identity flag 0x{EventTableFlag:X8} requests EVE-table membership. EOT_SetOfEOT_Build copies the activated-table identity snapshot into eventGameObjectsTablePF; this bit alone does not prove the owner reached that snapshot."
            : $"Serialized GAO identity flag 0x{EventTableFlag:X8} is clear; this snapshot does not request eventGameObjectsTablePF membership unless runtime code changes the identity flags before EOT construction.";
        GameScriptInitialActivationForecast activationForecast = GameScriptInitialActivationForecaster
            .Evaluate(owner.SerializedGaoFlags, owner.SerializedStateFlags);

        return
        [
            $"Linked SceneEvents EVE list: {owner.SceneEventsKey:X8}. Events {FormatKey(owner.EventsKey)} is an actor action/animation link and is not treated as a world EVE list here.",
            flagFinding,
            DescribeInitialActivation(activationForecast),
            $"EVE_MainCall advances the attached list only for a valid world-table entry with an EVE component, runtime flags2 0x{PendingInactiveFlag:X8} clear, and nonzero distance when extraFlag 0x{DistanceCullExtraFlag:X} is set. World activation, table membership, distance, and actual track execution are not proven by this BF snapshot."
        ];
    }

    private static string DescribeInitialActivation(GameScriptInitialActivationForecast forecast)
    {
        string[] paths = forecast.Paths.Select(path =>
        {
            string eotTables = path.EotMembership is { } membership && membership.TableNames.Length > 0
                ? $"; EOT categories if activated: {string.Join(", ", membership.TableNames)}"
                : "; no EOT category is forecast for this path";
            return $"{path.WorldMode}: {path.Eligibility} — {path.Reason}{eotTables}";
        }).ToArray();

        return "Fresh-load activation projection from serialized flags (not observed runtime state): "
            + string.Join(" | ", paths)
            + " Runtime changes, the actual EOT rebuild, and scheduler execution remain unknown.";
    }

    private static bool HasResourceKey(uint key) => key is > 2 and < uint.MaxValue;

    private static string FormatKey(uint key) => HasResourceKey(key) ? $"{key:X8}" : "none";
}
