namespace JadeView;

internal static class CinematicWorldCommandResolver
{
    internal static string DescribeSourceOperation(string operation) => operation switch
    {
        "Pause" => "Iris pauses matching active world objects by GAO identity flags and rebuilds world visibility tables",
        "UnPause" => "Iris resumes matching world objects, restores activation when permitted, and rebuilds world visibility tables",
        "PlayAbsTime" => "Iris reinitializes loaded worlds and advances them in special-time steps",
        _ => "Iris source invokes a world-scheduler operation"
    };

    internal static string Describe(CinematicLiteralCall call) =>
        CinematicEngineFunctionCatalog.NameFor(call.Selector) switch
        {
            "WORPause" when call.Arguments.Length >= 2 =>
                $"pause eligible objects in universe world slot 0x{call.Arguments[0]:X8} whose GAO flags match 0x{call.Arguments[1]:X8}; Iris sets runtime flags2 bit 0x04000000 except objects with bit 0x08000000, then requests world-table and visibility rebuilds",
            "WORUnPause" when call.Arguments.Length >= 2 =>
                $"unpause objects in universe world slot 0x{call.Arguments[0]:X8} whose GAO flags match 0x{call.Arguments[1]:X8}; Iris clears runtime flags2 bit 0x04000000, restores active bit 0x1 when activation gate 0x00400000 is clear, then requests world-table and visibility rebuilds",
            "WORPlayAbsTime" when call.Arguments.Length >= 2 =>
                $"set global ENG_gf_SpecialTime from argument 0x{call.Arguments[0]:X8} and ENG_gf_SpecialDT from 0x{call.Arguments[1]:X8}; Iris reinitializes loaded worlds and advances them in special-time steps until the budget expires, then clears both values",
            _ => ""
        };
}
