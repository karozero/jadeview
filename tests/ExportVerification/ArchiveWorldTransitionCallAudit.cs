using System.Text.Json;
using JadeView;

internal static class ArchiveWorldTransitionCallAudit
{
    private static readonly HashSet<string> TargetApis = new(StringComparer.Ordinal)
    {
        "WORLoadList",
        "WORUniverseAdd",
        "WORActive",
        "WORDesactive",
        "WORPlayAbsTime",
        "WORCurIndexGet",
        "WORPause",
        "WORUnPause"
    };

    private sealed record CallSite(string Container, string ContainerName, string WorldKey,
        string? WorldName, string OwnerKey, string OwnerName, string[] CallbackRoots,
        string CallbackPath, string FunctionName, string Api, string Variant, string[] Arguments,
        CinematicScriptArgumentAssignmentSource[] ArgumentAssignments,
        CinematicScriptArgumentAssignmentAlternative[]? ArgumentAssignmentAlternatives,
        CinematicScriptBranchConditionSource[] BranchConditions, string Source, int Line,
        string EvidenceKind);

    private sealed record ContainerFailure(string Container, string Name, string ErrorType, string Error);

    internal static bool IsTargetApi(string api) => TargetApis.Contains(api);

    internal static CinematicScriptReachableEngineCall[] SelectCalls(CinematicScriptOwner owner,
        IEnumerable<CinematicScriptReachableEngineCall>? calls = null) =>
        (calls ?? CinematicScriptReader.ReachableEngineCalls(owner))
            .Where(call => IsTargetApi(call.Api))
            .OrderBy(call => call.Api, StringComparer.Ordinal)
            .ThenBy(call => call.FunctionName, StringComparer.Ordinal)
            .ThenBy(call => call.Source, StringComparer.Ordinal)
            .ThenBy(call => call.Line)
            .ToArray();

    internal static void Write(string archivePath, string outputDirectory)
    {
        var archive = new Archive(archivePath);
        var entries = archive.Entries
            .Where(entry => (entry.Key & 0xfff00000) == 0xff000000)
            .OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var failures = new List<ContainerFailure>();
        var matches = new List<CallSite>();
        int loadedContainers = 0;
        int worldCount = 0;
        int ownerCount = 0;
        int ownersWithMatches = 0;

        foreach (var entry in entries)
        {
            SceneDocument document;
            try
            {
                document = SceneLoader.Load(archive, entry);
                loadedContainers++;
            }
            catch (Exception exception)
            {
                failures.Add(new(entry.Id, entry.Name, exception.GetType().Name, exception.Message));
                continue;
            }

            worldCount += document.Worlds.Count;
            var worldNames = document.Worlds.GroupBy(world => world.Key)
                .ToDictionary(group => group.Key, group => group.First().Name);
            foreach (var owner in document.CinematicScriptOwners.Values
                         .OrderBy(owner => owner.WorldKey).ThenBy(owner => owner.OwnerKey))
            {
                ownerCount++;
                var calls = SelectCalls(owner);
                if (calls.Length == 0) continue;
                ownersWithMatches++;
                string? worldName = worldNames.GetValueOrDefault(owner.WorldKey);
                matches.AddRange(calls.Select(call => new CallSite(
                    entry.Id,
                    entry.Name,
                    owner.WorldKey.ToString("X8"),
                    worldName,
                    owner.OwnerKey.ToString("X8"),
                    owner.Name,
                    owner.CallbackNames,
                    call.CallbackPath,
                    call.FunctionName,
                    call.Api,
                    call.Variant,
                    call.Arguments,
                    call.ArgumentAssignments,
                    call.ArgumentAssignmentAlternatives,
                    call.BranchConditions,
                    call.Source,
                    call.Line,
                    "reachable source callsite; not runtime dispatch")));
            }
        }

        Directory.CreateDirectory(outputDirectory);
        string outputPath = Path.Combine(outputDirectory, "archive-world-transition-call-audit.json");
        var report = new
        {
            EvidenceKind = "reachable source callsites; not runtime dispatch",
            Archive = Path.GetFullPath(archivePath),
            Scope = "all FFxxxxxx scene containers decoded through SceneLoader",
            TargetApis = TargetApis.Order(StringComparer.Ordinal).ToArray(),
            Coverage = new
            {
                SceneContainers = entries.Length,
                LoadedContainers = loadedContainers,
                FailedContainers = failures.Count,
                Worlds = worldCount,
                ScriptOwners = ownerCount,
                OwnersWithTargetCallsites = ownersWithMatches,
                TargetCallsites = matches.Count
            },
            Matches = matches,
            Failures = failures
        };
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        Console.WriteLine($"World transition source audit: {loadedContainers}/{entries.Length} containers, "
            + $"{ownerCount} owners, {matches.Count} reachable callsites, {failures.Count} load failures.");
        Console.WriteLine($"Wrote {outputPath}");
    }
}
