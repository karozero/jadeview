using System.Text.Json;
using JadeView;

internal static class PcAiFunctionCallGraphAudit
{
    private const int MaximumFunctionBytes = 16 * 1024 * 1024;

    internal static void Write(string archivePath, string rootKeyText, string output)
    {
        uint rootKey = Convert.ToUInt32(rootKeyText, 16);
        var archive = new Archive(archivePath);
        ArchiveStartupScriptInventory inventory = ArchiveStartupScriptReader.Read(archive);
        Dictionary<uint, string> linkedNames = inventory.ModelResources
            .Where(resource => resource.TypeTag == ".ofc" && resource.Status == "Resolved" && resource.Name != null)
            .GroupBy(resource => resource.Key)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Name!);

        Entry[] rootEntries = archive.Entries.Where(entry => entry.Key == rootKey).Take(2).ToArray();
        if (rootEntries.Length != 1 || !linkedNames.TryGetValue(rootKey, out string? rootName))
            throw new InvalidDataException($"Root OFC {rootKey:X8} is missing, ambiguous, or not linked by the archive's global AI model.");

        PcSerializedAiFunction root = ReadFunction(archive, rootEntries[0]);
        var cache = new Dictionary<uint, PcSerializedAiFunction> { [rootKey] = root };
        PcAiFunctionCallPath[] paths = PcAiFunctionCallGraphAnalyzer.Analyze(rootKey, rootName, root,
            linkedNames, ResolveFunction);
        string path = Path.Combine(output, $"pc-ai-call-graph-{rootKey:X8}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            Archive = Path.GetFileName(archive.PathName),
            RootKey = $"{rootKey:X8}",
            RootName = rootName,
            Evidence = "Static linked AIExecute references only; stack values, branch outcomes and runtime dispatch are not evaluated.",
            Paths = paths.Select(callPath => new
            {
                FunctionKeys = callPath.FunctionKeys.Select(key => key.ToString("X8")),
                callPath.FunctionNames,
                callPath.CallNodeIndexes,
                callPath.TransferKinds,
                callPath.Status
            })
        }, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"Root: {rootName} · {rootKey:X8}");
        foreach (PcAiFunctionCallPath callPath in paths)
        {
            string chain = string.Join(" → ", callPath.FunctionNames);
            string transferPath = callPath.CallNodeIndexes.Length > 0
                ? $" · {string.Join(" → ", callPath.TransferKinds.Zip(callPath.CallNodeIndexes,
                    (kind, nodeIndex) => $"{kind}@{nodeIndex}"))}" : string.Empty;
            Console.WriteLine($"{chain}{transferPath} · {callPath.Status}");
        }
        Console.WriteLine($"Audit: {path}");

        PcSerializedAiFunction? ResolveFunction(uint key)
        {
            if (cache.TryGetValue(key, out PcSerializedAiFunction? cached)) return cached;
            Entry[] entries = archive.Entries.Where(entry => entry.Key == key).Take(2).ToArray();
            if (entries.Length != 1) return null;
            try
            {
                PcSerializedAiFunction function = ReadFunction(archive, entries[0]);
                cache[key] = function;
                return function;
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }
    }

    private static PcSerializedAiFunction ReadFunction(Archive archive, Entry entry)
    {
        byte[] payload = archive.Read(entry, MaximumFunctionBytes);
        if (PcSerializedAiFunctionReader.TryRead(payload, out PcSerializedAiFunction? function, out string error)
            && function != null)
            return function;
        throw new InvalidDataException(error);
    }
}
