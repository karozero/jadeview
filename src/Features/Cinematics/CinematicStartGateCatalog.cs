namespace JadeView;

/// <summary>
/// Shared Iris Cine_Events gate bits and their serialized OVA fields. The behavior
/// analyzer and preview evaluator present different evidence, but inspect the same
/// configured start-gate table.
/// </summary>
internal static class CinematicStartGateCatalog
{
    internal const int Zone1 = 0x001;
    internal const int Zone2 = 0x002;
    internal const int Activity = 0x004;
    internal const int Capability = 0x008;
    internal const int CustomBits = 0x010;
    internal const int Message = 0x020;
    internal const int Visibility = 0x040;
    internal const int SpecialFlag1 = 0x080;
    internal const int SpecialFlag2 = 0x100;
    internal const int Destructible = 0x200;

    internal static readonly CinematicStartGateDefinition[] StartGates =
    [
        new(Zone1, "des_o_actor_zone1", "zone 1 collision / optional model filter", "Zone 1 collision",
            ["resolved des_o_actor_detec1", "runtime collision overlap/query result"]),
        new(Zone2, "des_o_actor_zone2", "zone 2 collision relation", "Zone 2 relation",
            ["resolved des_o_actor_detec2", "runtime zone identity flag 0x400", "runtime collision query result"]),
        new(Activity, "des_o_actor_status1", "actor activity state", "Actor activity",
            ["target runtime flags2 bit 0 (activated-world state)", "des_i_activity_test1"]),
        new(Capability, "des_o_actor_capa1", "actor capability mask", "Actor capabilities", []),
        new(CustomBits, "des_o_actor_cbit1", "actor custom-bit mask", "Actor custom bits",
            ["target serialized custom bits 8–15", "unambiguous timeline writes to the target"]),
        new(Message, "des_o_acteur_msg", "queued message ID and optional Int1 mask", "Queued message",
            ["runtime message queue", "message ID and Int1"]),
        new(Visibility, "des_o_actor_visible1", "actor visibility and viewport bounds", "Actor visibility",
            ["runtime visibility", "camera/viewport bounds"]),
        new(SpecialFlag1, "des_i_specialFlag_test1", "saved special flag test 1", "Saved special flag test 1", []),
        new(SpecialFlag2, "des_i_specialFlag_test2", "saved special flag test 2", "Saved special flag test 2", []),
        new(Destructible, "des_o_actor_desstruct", "destructible actor state", "Destructible actor state",
            ["runtime destructible state"])
    ];

    internal static readonly (int Mask, string Variable, string Description)[] InterruptionGates =
    [
        (Zone1, "des_o_actor_zone_exit1", "configured actor exit-zone collision test"),
        (Activity, "des_o_actor_status_exit1", "actor activity-state exit test"),
        (Capability, "des_o_actor_capa_exit1", "actor capability exit test"),
        (SpecialFlag1, "des_i_SPFlag_test_exit1", "saved special-flag exit test")
    ];
}

internal sealed record CinematicStartGateDefinition(
    int Mask,
    string Variable,
    string Description,
    string Label,
    string[] MissingInputs);
