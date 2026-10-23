namespace JadeView;

internal static class CinematicWorldCommandResolver
{
    internal static string DescribeSourceOperation(string operation)
    {
        string normalizedOperation = operation.StartsWith("WOR", StringComparison.Ordinal)
            ? operation[3..] : operation;
        return normalizedOperation switch
        {
        "Pause" => "Iris pauses matching active world objects by GAO identity flags and rebuilds world visibility tables",
        "UnPause" => "Iris resumes matching world objects, restores activation when permitted, and rebuilds world visibility tables",
        "PlayAbsTime" => "Iris reinitializes loaded worlds and advances them in special-time steps",
        "LoadList" => "Iris clears the current universe and global world resources, then loads and merges the requested world-list",
        "UniverseAdd" => "Iris loads a world, initializes it, resolves and activates its objects, and adds it to the universe",
        "Active" => "Iris marks a loaded universe world active and makes it the current world",
        "Desactive" => "Iris clears the active flag on a loaded universe world",
        "UniverseDestroy" => "Iris deletes a loaded universe world; its second serialized integer is consumed but ignored",
        "GetObject" => "Iris resolves an object-table slot inside a loaded universe world",
        "RefreshVisible" => "Iris rebuilds visibility tables for a loaded universe world",
        "RefreshVisibleOfActor" => "Iris rebuilds visibility tables for the current object's world",
        "Loaded" => "Iris implementation returns constant 1; it does not check whether a world is loaded",
            _ => ""
        };
    }

    internal static bool IsWorldManagementOperation(string operation) => operation.StartsWith("WOR",
        StringComparison.Ordinal) && DescribeSourceOperation(operation).Length > 0;

    internal static string Describe(CinematicLiteralCall call) =>
        CinematicEngineFunctionCatalog.NameFor(call.Selector) switch
        {
            "WORPause" when call.Arguments.Length >= 2 =>
                $"pause eligible objects in universe world slot 0x{call.Arguments[0]:X8} whose GAO flags match 0x{call.Arguments[1]:X8}; Iris sets runtime flags2 bit 0x04000000 except objects with bit 0x08000000, then requests world-table and visibility rebuilds",
            "WORUnPause" when call.Arguments.Length >= 2 =>
                $"unpause objects in universe world slot 0x{call.Arguments[0]:X8} whose GAO flags match 0x{call.Arguments[1]:X8}; Iris clears runtime flags2 bit 0x04000000, restores active bit 0x1 when activation gate 0x00400000 is clear, then requests world-table and visibility rebuilds",
            "WORPlayAbsTime" when call.Arguments.Length >= 2 =>
                $"set global ENG_gf_SpecialTime from argument 0x{call.Arguments[0]:X8} and ENG_gf_SpecialDT from 0x{call.Arguments[1]:X8}; Iris reinitializes loaded worlds and advances them in special-time steps until the budget expires, then clears both values",
            "WORLoadList" when call.Arguments.Length >= 1 =>
                $"replace the current world universe with world-list key 0x{call.Arguments[0]:X8}; PC Iris deletes loaded worlds and resets global world resources before loading and merging this list",
            "WORUniverseAdd" when call.Arguments.Length >= 1 =>
                $"load world key 0x{call.Arguments[0]:X8}, initialize and activate its objects, then add it to the universe (returns its world slot)",
            "WORActive" when call.Arguments.Length >= 1 =>
                $"make loaded universe world slot {call.Arguments[0]} active and set it as the current world",
            "WORDesactive" when call.Arguments.Length >= 1 =>
                $"clear the active flag on loaded universe world slot {call.Arguments[0]}",
            "WORUniverseDestroy" when call.Arguments.Length >= 2 =>
                $"delete loaded universe world slot {call.Arguments[0]} (PC Iris consumes but ignores serialized flags 0x{call.Arguments[1]:X8})",
            "WORGetObject" when call.Arguments.Length >= 2 =>
                $"resolve object-table slot {call.Arguments[1]} in loaded universe world slot {call.Arguments[0]}",
            "WORRefreshVisible" when call.Arguments.Length >= 1 =>
                $"rebuild visibility tables for loaded universe world slot {call.Arguments[0]}",
            "WORRefreshVisibleOfActor" =>
                "rebuild visibility tables for the current execution object's world",
            "WORLoaded" =>
                "return 1 (PC Iris stub; this call does not verify that a world is loaded)",
            _ => ""
        };
}
