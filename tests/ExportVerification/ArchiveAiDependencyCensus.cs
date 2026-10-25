using System.Text.Json;
using JadeView;

internal static class ArchiveAiDependencyCensus
{
    private sealed record OwnerRow(
        string Container,
        string WorldKey,
        string WorldName,
        string OwnerKey,
        string OwnerName,
        string SerializedIdentityFlags,
        string BrainKey,
        string ModelKey,
        string[] CallbackSlots,
        string[] EngineCallbackSlots,
        string[] FunctionResources,
        string EventsKey,
        string SceneEventsKey,
        string[] InstanceOverrides,
        string[] ObjectReferences);

    internal static void Write(string archivePath, string outputDirectory)
    {
        var archive = new Archive(archivePath);
        var owners = new List<OwnerRow>();
        int scannedContainers = 0;
        int parseErrors = 0;

        foreach (var entry in archive.Entries.Where(entry => (entry.Key & 0xfff00000) == 0xff000000)
                     .OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase))
        {
            byte[] bytes = archive.Read(entry);
            var replay = PcSceneLoader.Read(bytes, archivePath, entry.Key);
            if (!replay.Complete)
            {
                parseErrors++;
                continue;
            }

            scannedContainers++;
            SceneDocument document = SceneLoader.Load(bytes, entry.Name, archivePath, worldListKey: entry.Key);
            foreach (CinematicScriptOwner owner in document.CinematicScriptOwners.Values)
            {
                var world = document.Worlds.FirstOrDefault(candidate => candidate.Key == owner.WorldKey);
                owners.Add(new OwnerRow(
                    entry.Id,
                    owner.WorldKey.ToString("X8"),
                    world?.Name ?? "",
                    owner.OwnerKey.ToString("X8"),
                    owner.Name,
                    $"0x{owner.SerializedGaoFlags:X8}",
                    owner.BrainKey.ToString("X8"),
                    owner.ScriptModelKey.ToString("X8"),
                    (owner.CallbackSlots ?? []).Select(slot =>
                        $"{slot.Index}:{slot.FunctionKey:X8}:{slot.FunctionName ?? "unmapped"}").ToArray(),
                    (owner.EngineCallbackSlots ?? []).Select(slot =>
                        $"{slot.EventId}:{slot.FunctionKey:X8}:{slot.FunctionName ?? "unmapped"}").ToArray(),
                    owner.FunctionResources.Select(resource =>
                        $"{resource.FunctionKey:X8}:{resource.Status}:{resource.Name}").ToArray(),
                    FormatKey(owner.EventsKey),
                    FormatKey(owner.SceneEventsKey),
                    owner.Variables.Where(variable => variable.Source == "instance")
                        .Select(variable => $"{variable.Name}={variable.Value}").ToArray(),
                    owner.ObjectReferences.Select(reference =>
                        $"{reference.VariableName}[{reference.ArrayIndex?.ToString() ?? "-"}]={reference.TargetKey:X8}:{reference.TargetName ?? "unresolved"}")
                        .ToArray()));
            }
        }

        OwnerRow[] orderedOwners = owners.OrderBy(owner => owner.Container, StringComparer.OrdinalIgnoreCase)
            .ThenBy(owner => owner.WorldKey, StringComparer.Ordinal)
            .ThenBy(owner => owner.OwnerKey, StringComparer.Ordinal).ToArray();
        var report = new
        {
            Archive = Path.GetFileName(archivePath),
            ScannedContainers = scannedContainers,
            ContainerParseErrors = parseErrors,
            ScriptOwnerRecordCount = orderedOwners.Length,
            DistinctWorldOwnerKeys = orderedOwners.Select(owner => $"{owner.WorldKey}:{owner.OwnerKey}")
                .Distinct(StringComparer.Ordinal).Count(),
            DistinctBrains = orderedOwners.Select(owner => owner.BrainKey).Distinct(StringComparer.Ordinal).Count(),
            DistinctModels = orderedOwners.Select(owner => owner.ModelKey).Distinct(StringComparer.Ordinal).Count(),
            OwnersWithCallbackSlots = orderedOwners.Count(owner => owner.CallbackSlots.Length > 0),
            OwnersWithEngineCallbacks = orderedOwners.Count(owner => owner.EngineCallbackSlots.Length > 0),
            OwnersWithEvents = orderedOwners.Count(owner => owner.EventsKey != "none"),
            OwnersWithSceneEvents = orderedOwners.Count(owner => owner.SceneEventsKey != "none"),
            FunctionResourceOccurrences = orderedOwners.Sum(owner => owner.FunctionResources.Length),
            DistinctFunctionResourceKeys = orderedOwners.SelectMany(owner => owner.FunctionResources)
                .Select(resource => resource.Split(':', 2)[0])
                .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            SerializedFunctionBodyOccurrences = CountFunctionResources("SerializedBody"),
            CompactMetadataCandidateOccurrences = CountFunctionResources("CompactMetadataCandidate"),
            UnsupportedFunctionResourceOccurrences = CountFunctionResources("UnsupportedLayout"),
            FunctionResourceStatuses = orderedOwners.SelectMany(owner => owner.FunctionResources)
                .Select(resource => resource.Split(':', 3))
                .Where(parts => parts.Length == 3)
                .GroupBy(parts => parts[1], StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new { Status = group.Key, Count = group.Count() }).ToArray(),
            Rows = orderedOwners,
            Evidence = new
            {
                Confirmed = "Rows contain serialized owner-to-brain/model links, decoded callback slots, typed Events/SceneEvents links, effective instance variables and model function-resource resolution available to JadeView.",
                InferenceLimit = "A resolved key or callback slot does not prove that the retail game loaded the same resource, activated the owner, registered a runtime track, passed its gates, or executed it. Function names are mapped from Iris source where available; Jaded is comparative engine evidence and may differ from the retail PC build.",
                Sources = "JadeView ArchiveCinematicScriptIndex and CinematicScriptReader; external/irisEng/ai_common.c model/instance loaders; external/jaded/src/libraries/AIinterp/Sources/AIload.c and external/jaded/src/libraries/ENGine/Sources/OBJects/OBJload.c"
            }
        };

        Directory.CreateDirectory(outputDirectory);
        string output = Path.Combine(outputDirectory, "archive-ai-dependency-census.json");
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"AI DEPENDENCY CENSUS: {orderedOwners.Length} owners, {report.DistinctBrains} brains, "
            + $"{report.DistinctModels} models across {scannedContainers} containers -> {output}");

        int CountFunctionResources(string status) => orderedOwners.SelectMany(owner => owner.FunctionResources)
            .Count(resource => resource.Split(':', 3) is { Length: 3 } parts && parts[1] == status);
    }

    private static string FormatKey(uint key) => key is <= 2 or uint.MaxValue ? "none" : key.ToString("X8");
}
