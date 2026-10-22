namespace JadeView;

internal static partial class CinematicScriptBehaviorAnalyzer
{
    private const string WorldTransitionEventCallback = "ES_au_sol_esas_play_events";

    private static void AddWorldTransitionEventBehavior(List<string> diagnostics)
    {
        diagnostics.Add("ES_au_sol_esas_play_events is a shared world/companion transition controller. "
            + "At jump node 10, the source branch checks capability 0x100 and identity flag 0x02000000; "
            + "when both pass it freezes player input and issues TRAPlay requests for SceneEvents indices 3, 4 and 5 "
            + "on this owner. Otherwise it clears the input freeze and switches callback tracks 1 and 2 to the reflex and Mdisk handlers.");
        diagnostics.Add("At jump node 8, the Mdisk travel branch can run the configured status change, "
            + "write the requested destination map and entrance into shared world-load state, then stop this AI track. "
            + "The archive exposes configured fields and event channels, but not the live jump node, identity/capability "
            + "state, or whether this callback branch ran.");
    }
}
