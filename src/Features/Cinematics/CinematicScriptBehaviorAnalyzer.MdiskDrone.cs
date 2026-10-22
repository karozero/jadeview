namespace JadeView;

internal static partial class CinematicScriptBehaviorAnalyzer
{
    private static void AddMdiskDroneBehavior(List<string> diagnostics, CinematicScriptOwner owner)
    {
        uint? verticalPartKey = EffectiveUnsigned(owner, "des_o_drone_vert");
        var verticalPart = owner.EffectiveObjectReference("des_o_drone_vert");
        diagnostics.Add(verticalPartKey switch
        {
            0 => "des_o_drone_vert is explicitly null, so this Drone_Mdisk instance has no separately configured vertical component to align.",
            > 0 when verticalPart != null => $"Drone_Mdisk init aligns and parents {DescribeObjectReference(verticalPart)} to the owner, then initially marks that component inactive and invisible. This describes the callback's initialization commands, not its live state.",
            > 0 => $"des_o_drone_vert points to {verticalPartKey:X8}, which is unresolved in the loaded archive; its component alignment cannot be traced.",
            _ => "Drone_Mdisk init aligns and parents the configured des_o_drone_vert component to the owner; that OVA reference is unavailable."
        });

        int? movementFlag = EffectiveInteger(owner, "SF_deplacement_done");
        var plugWaypoint = owner.EffectiveObjectReference("des_o_plug_wp");
        if (movementFlag is null)
        {
            diagnostics.Add("Drone_Mdisk init selects its plug or wait mode using saved flag ID SF_deplacement_done, but that configured ID is unavailable.");
            return;
        }

        if (movementFlag <= 0)
        {
            diagnostics.Add($"SF_deplacement_done={movementFlag}: Drone_Mdisk init does not test a saved flag and continues to mode_wait.");
            return;
        }

        uint? plugWaypointKey = EffectiveUnsigned(owner, "des_o_plug_wp");
        if (plugWaypointKey == 0)
        {
            diagnostics.Add($"SF_deplacement_done={movementFlag}: if that saved flag is set, des_o_plug_wp is explicitly null and the source destroys the owner; if the flag is clear, it enters mode_wait. Current save state is unavailable.");
            return;
        }

        if (plugWaypoint == null)
        {
            diagnostics.Add($"SF_deplacement_done={movementFlag}: if that saved flag is set, des_o_plug_wp={plugWaypointKey?.ToString("X8") ?? "unavailable"} cannot be resolved in the loaded archive; the source destroys the owner when the reference is null at runtime. If the flag is clear, it enters mode_wait. Current save state is unavailable.");
            return;
        }

        diagnostics.Add($"SF_deplacement_done={movementFlag}: if that saved flag is set, Drone_Mdisk init aligns the owner to {DescribeObjectReference(plugWaypoint)} and enters mode_plug; if the reference cannot resolve at runtime, the source destroys the owner. If the saved flag is clear, it enters mode_wait. Current save state is unavailable.");
    }
}
