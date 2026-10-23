namespace JadeView;

internal static class GameScriptWorldOperationDescriptions
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

}
