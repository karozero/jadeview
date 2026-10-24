using JadeView;

internal static class ScriptFunctionMetadataAudit
{
    internal static void Write(SceneDocument document)
    {
        var resources = document.CinematicScriptOwners.Values
            .SelectMany(owner => owner.FunctionResources)
            .DistinctBy(resource => (resource.WorldKey, resource.FunctionKey))
            .OrderBy(resource => resource.WorldKey).ThenBy(resource => resource.FunctionKey)
            .ToArray();
        Console.WriteLine($"SCRIPT FUNCTION INVENTORY: resources={resources.Length}; "
            + string.Join("; ", resources.GroupBy(resource => resource.Status)
                .Select(group => $"{group.Key}={group.Count()}")));
        foreach (var resource in resources)
        {
            Console.WriteLine($"SCRIPT FUNCTION: world={resource.WorldKey:X8} key={resource.FunctionKey:X8} "
                + $"name={resource.Name} bytes={resource.PayloadSize} status={resource.Status} "
                + $"stack={resource.Metadata?.StackOffsetCount} declaredNodeBytes={resource.Metadata?.DeclaredNodeByteSize} "
                + $"localConstantBytes={resource.Metadata?.LocalConstantBytes.Length} diagnostic={resource.Diagnostic}");
        }
    }
}
