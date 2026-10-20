using System.Collections;
using System.Security.Cryptography;
using System.Text.Json;
using JadeView;

internal static class ArchiveFacialScriptCensus
{
    internal sealed record SourceRecord(string Container, byte[] Source, uint Key, char Kind,
        int Offset, int Size, uint World, string Name, uint? Brain, uint[] References);

    private sealed record TriggerCandidate(string Container, uint Owner, string OwnerName,
        string Clip, string? World, string TrackTarget, float Time);

    internal static void Write(string archivePath, string candidatesPath, string output)
    {
        using var candidateDocument = JsonDocument.Parse(File.ReadAllText(candidatesPath));
        var candidateGroups = candidateDocument.RootElement.GetProperty("matches").EnumerateArray()
            .GroupBy(match => (Container: match.GetProperty("container").GetString()!,
                Target: Convert.ToUInt32(match.GetProperty("trackTarget").GetString(), 16)))
            .ToDictionary(group => group.Key, group => group.Select(match => new
            {
                Clip = match.GetProperty("clip").GetString(),
                AnimationWorldKey = match.GetProperty("animationWorldKey").GetString(),
                AnimationWorld = match.GetProperty("animationWorld").GetString(),
                Time = match.GetProperty("time").GetSingle(),
                SourceOffset = match.GetProperty("sourceOffset").GetInt32()
            }).ToArray());
        var triggerCandidates = candidateDocument.RootElement.GetProperty("matches").EnumerateArray()
            .SelectMany(match => match.GetProperty("clipOwners").EnumerateArray().Select(owner =>
                new TriggerCandidate(
                    match.GetProperty("container").GetString()!,
                    Convert.ToUInt32(owner.GetProperty("Key").GetString(), 16),
                    owner.GetProperty("Name").GetString()!,
                    match.GetProperty("clip").GetString()!,
                    match.GetProperty("animationWorld").GetString(),
                    match.GetProperty("trackTarget").GetString()!,
                    match.GetProperty("time").GetSingle())))
            .ToArray();

        var archive = new Archive(archivePath);
        var fixedEntry = archive.Entries.FirstOrDefault(entry => entry.Id == "FF0084DF");
        var fixedRecords = fixedEntry == null ? [] : ReadRecords(archive.Read(fixedEntry), archive.PathName, fixedEntry.Id);
        var summaries = new List<object>();
        int containerIndex = 0;
        var containerNames = candidateGroups.Keys.Select(key => key.Container)
            .Concat(triggerCandidates.Select(candidate => candidate.Container)).Distinct().Order().ToArray();
        foreach (string containerName in containerNames)
        {
            var entry = archive.Entries.Single(candidate => candidate.Id == containerName);
            byte[] source = archive.Read(entry);
            var sceneRecords = ReadRecords(source, archive.PathName, entry.Id);
            var worldNames = sceneRecords.Where(record => record.Kind == 'z')
                .GroupBy(record => record.Key)
                .ToDictionary(group => group.Key, group => group.First().Name);
            var sceneObjects = sceneRecords.Where(record => record.Kind == 'l').ToArray();
            var rootsByKey = sceneObjects.Where(record => record.Brain.HasValue)
                .GroupBy(record => record.Key)
                .ToDictionary(group => group.Key, group => group.First());
            var actors = new List<object>();

            foreach (var candidateGroup in candidateGroups.Where(group => group.Key.Container == containerName))
            {
                uint target = candidateGroup.Key.Target;
                if (!rootsByKey.TryGetValue(target, out var actor))
                {
                    actors.Add(new
                    {
                        Target = target.ToString("X8"),
                        Name = (string?)null,
                        Brain = (string?)null,
                        WorldKey = (string?)null,
                        WorldName = (string?)null,
                        CandidateEvents = candidateGroup.Value,
                        Missing = Array.Empty<string>(),
                        Closure = Array.Empty<object>(),
                        Limits = "No same-container GAO with a parsed brain field matched this track target."
                    });
                    continue;
                }

                var eligibleResources = sceneRecords.Concat(fixedRecords)
                    .Where(record => record.Kind is 'q' or 's' or 't' or 'r')
                    .Where(record => record.Container == "FF0084DF" || record.World is 0 || record.World == actor.World)
                    .GroupBy(record => record.Key)
                    .ToDictionary(group => group.Key, group => group
                        .OrderByDescending(record => record.Container == containerName)
                        .First());
                var pending = new Queue<uint>();
                pending.Enqueue(actor.Brain!.Value);
                var visited = new HashSet<uint>();
                var closure = new List<SourceRecord>();
                var missing = new List<uint>();
                var crossWorldKeys = new HashSet<uint>();
                while (pending.TryDequeue(out uint resourceKey))
                {
                    if (resourceKey is 0 or uint.MaxValue || !visited.Add(resourceKey)) continue;
                    if (!eligibleResources.TryGetValue(resourceKey, out var resource))
                    {
                        var sameKeyMatches = sceneRecords.Concat(fixedRecords)
                            .Where(candidate => candidate.Key == resourceKey && candidate.Kind is 'q' or 's' or 't' or 'r')
                            .ToArray();
                        var variants = sameKeyMatches.GroupBy(candidate =>
                            $"{candidate.Kind}:{Convert.ToHexString(SHA256.HashData(candidate.Source.AsSpan(candidate.Offset, candidate.Size)))}")
                            .ToArray();
                        if (variants.Length != 1)
                        {
                            missing.Add(resourceKey);
                            continue;
                        }
                        resource = variants[0].OrderByDescending(candidate => candidate.Container == containerName).First();
                        crossWorldKeys.Add(resourceKey);
                    }
                    closure.Add(resource);
                    foreach (uint reference in resource.References) pending.Enqueue(reference);
                }

                actors.Add(new
                {
                    Target = target.ToString("X8"),
                    actor.Name,
                    Brain = actor.Brain.Value.ToString("X8"),
                    WorldKey = actor.World.ToString("X8"),
                    WorldName = worldNames.GetValueOrDefault(actor.World),
                    CandidateEvents = candidateGroup.Value,
                    Missing = missing.Select(key =>
                    {
                        var matches = FindTypedMatches(sceneRecords.Concat(fixedRecords), key);
                        return new { Key = key.ToString("X8"), OtherContainerOrWorldMatches = matches };
                    }).ToArray(),
                    CrossWorldResources = closure.Where(record => crossWorldKeys.Contains(record.Key))
                        .Select(record => new
                        {
                            Key = record.Key.ToString("X8"),
                            Kind = record.Kind.ToString(),
                            record.Container,
                            WorldKey = record.World.ToString("X8"),
                            record.Size,
                            Sha256 = Convert.ToHexString(SHA256.HashData(record.Source.AsSpan(record.Offset, record.Size)))
                        })
                        .ToArray(),
                    CodeKeys = closure.Where(record => record.Kind == 'r')
                        .Select(record => record.Key.ToString("X8")).Distinct().Order().ToArray(),
                    Closure = closure.Select(record => new
                    {
                        record.Container,
                        Key = record.Key.ToString("X8"),
                        Kind = record.Kind.ToString(),
                        record.Offset,
                        record.Size,
                        Sha256 = Convert.ToHexString(SHA256.HashData(record.Source.AsSpan(record.Offset, record.Size))),
                        World = record.World.ToString("X8"),
                        record.Name,
                        Bytes = record.Kind is 'q' or 's' or 't'
                            ? Convert.ToHexString(record.Source.AsSpan(record.Offset, record.Size)) : null,
                        References = record.References.Select(key => key.ToString("X8")).ToArray()
                    }).ToArray(),
                    Limits = "Parsed brain dependency closure only. Compiled code resources are inventoried, not executed; this does not prove that a listed routine sends message 53. A missing key may exist as a typed resource in another container or world; those same-key matches are listed but not automatically merged into the closure."
                });
            }

            var triggerObjects = new List<object>();
            foreach (var triggerGroup in triggerCandidates.Where(candidate => candidate.Container == containerName)
                .GroupBy(candidate => candidate.Owner))
            {
                uint ownerKey = triggerGroup.Key;
                if (!rootsByKey.TryGetValue(ownerKey, out var owner))
                {
                    triggerObjects.Add(new
                    {
                        Container = containerName,
                        Key = ownerKey.ToString("X8"),
                        Name = triggerGroup.First().OwnerName,
                        Brain = (string?)null,
                        CandidateEvents = triggerGroup.ToArray(),
                        Missing = Array.Empty<string>(),
                        Closure = Array.Empty<object>(),
                        Limits = "The linked GAO did not expose a parsed brain field in this container."
                    });
                    continue;
                }

                var eligibleResources = sceneRecords.Concat(fixedRecords)
                    .Where(record => record.Kind is 'q' or 's' or 't' or 'r')
                    .Where(record => record.Container == "FF0084DF" || record.World is 0 || record.World == owner.World)
                    .GroupBy(record => record.Key)
                    .ToDictionary(group => group.Key, group => group
                        .OrderByDescending(record => record.Container == containerName)
                        .First());
                var pending = new Queue<uint>();
                pending.Enqueue(owner.Brain!.Value);
                var visited = new HashSet<uint>();
                var closure = new List<SourceRecord>();
                var missing = new List<uint>();
                var crossWorldKeys = new HashSet<uint>();
                while (pending.TryDequeue(out uint resourceKey))
                {
                    if (resourceKey is 0 or uint.MaxValue || !visited.Add(resourceKey)) continue;
                    if (!eligibleResources.TryGetValue(resourceKey, out var resource))
                    {
                        var sameKeyMatches = sceneRecords.Concat(fixedRecords)
                            .Where(candidate => candidate.Key == resourceKey && candidate.Kind is 'q' or 's' or 't' or 'r')
                            .ToArray();
                        var variants = sameKeyMatches.GroupBy(candidate =>
                            $"{candidate.Kind}:{Convert.ToHexString(SHA256.HashData(candidate.Source.AsSpan(candidate.Offset, candidate.Size)))}")
                            .ToArray();
                        if (variants.Length != 1)
                        {
                            missing.Add(resourceKey);
                            continue;
                        }
                        resource = variants[0].OrderByDescending(candidate => candidate.Container == containerName).First();
                        crossWorldKeys.Add(resourceKey);
                    }
                    closure.Add(resource);
                    foreach (uint reference in resource.References) pending.Enqueue(reference);
                }

                object? variableDefaults = null;
                string? variableDefaultsError = null;
                try
                {
                    variableDefaults = CinematicOwnerDefaults.Read(owner.Brain!.Value, closure);
                }
                catch (InvalidDataException error)
                {
                    variableDefaultsError = error.Message;
                }

                triggerObjects.Add(new
                {
                    Container = containerName,
                    Key = ownerKey.ToString("X8"),
                    owner.Name,
                    Brain = owner.Brain!.Value.ToString("X8"),
                    WorldKey = owner.World.ToString("X8"),
                    WorldName = worldNames.GetValueOrDefault(owner.World),
                    CandidateEvents = triggerGroup.ToArray(),
                    Missing = missing.Select(key => new
                    {
                        Key = key.ToString("X8"),
                        OtherContainerOrWorldMatches = FindTypedMatches(sceneRecords.Concat(fixedRecords), key)
                    }).ToArray(),
                    CrossWorldResources = closure.Where(record => crossWorldKeys.Contains(record.Key))
                        .Select(record => new
                        {
                            Key = record.Key.ToString("X8"),
                            Kind = record.Kind.ToString(),
                            record.Container,
                            WorldKey = record.World.ToString("X8"),
                            record.Size,
                            Sha256 = Convert.ToHexString(SHA256.HashData(record.Source.AsSpan(record.Offset, record.Size)))
                        })
                        .ToArray(),
                    CodeKeys = closure.Where(record => record.Kind == 'r')
                        .Select(record => record.Key.ToString("X8")).Distinct().Order().ToArray(),
                    VariableDefaults = variableDefaults,
                    VariableDefaultsError = variableDefaultsError,
                    Closure = closure.Select(record => new
                    {
                        record.Container,
                        Key = record.Key.ToString("X8"),
                        Kind = record.Kind.ToString(),
                        record.Offset,
                        record.Size,
                        Sha256 = Convert.ToHexString(SHA256.HashData(record.Source.AsSpan(record.Offset, record.Size))),
                        World = record.World.ToString("X8"),
                        record.Name,
                        Bytes = record.Kind is 'q' or 's' or 't'
                            ? Convert.ToHexString(record.Source.AsSpan(record.Offset, record.Size)) : null,
                        References = record.References.Select(key => key.ToString("X8")).ToArray()
                    }).ToArray(),
                    Limits = "Parsed brain dependency closure for an object that references a candidate animation. A reference does not prove the object starts the clip; code is not executed."
                });
            }

            summaries.Add(new { Container = containerName, Actors = actors, TriggerObjects = triggerObjects });
            containerIndex++;
            Console.WriteLine($"ARCHIVE FACIAL SCRIPT CENSUS: {containerIndex}/{containerNames.Length} containers");
        }

        var report = new
        {
            Archive = archive.PathName,
            CandidateReport = Path.GetFullPath(candidatesPath),
            ContainersScanned = summaries.Count,
            Limits = "Only same-container targets from literal type-53 event candidates are followed. Script closures use the actor's scene world plus shared world-zero and fixed FF0084DF q/s/t/r resources. No script execution, call attribution, runtime message delivery, or audio association is inferred.",
            Containers = summaries
        };
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "archive-facial-script-census.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static List<SourceRecord> ReadRecords(byte[] bytes, string archivePath, string container)
    {
        object replay = ReadScene(bytes, archivePath);
        var records = new List<SourceRecord>();
        foreach (object record in (IEnumerable)GetField(replay, "Records")!)
        {
            int offset = (int)GetField(record, "Offset")!;
            int size = (int)GetField(record, "Size")!;
            int brainOffset = (int)GetField(record, "BrainFieldOffset")!;
            var references = ((IEnumerable)GetField(record, "References")!).Cast<object>()
                .Select(reference => (uint)reference.GetType().GetProperty("Key")!.GetValue(reference)!).ToArray();
            records.Add(new(container, bytes, (uint)GetField(record, "Key")!, (char)GetField(record, "Kind")!,
                offset, size, (uint)GetField(record, "LoadWorldKey")!, (string)GetField(record, "Name")!,
                brainOffset >= 0 ? BitConverter.ToUInt32(bytes, brainOffset) : null, references));
        }
        return records;
    }

    private static object[] FindTypedMatches(IEnumerable<SourceRecord> records, uint key) => records
        .Where(record => record.Key == key)
        .Select(record => new
        {
            record.Container,
            Kind = record.Kind.ToString(),
            Key = record.Key.ToString("X8"),
            record.Name,
            WorldKey = record.World.ToString("X8"),
            record.Size,
            Sha256 = Convert.ToHexString(SHA256.HashData(record.Source.AsSpan(record.Offset, record.Size))),
            References = record.References.Select(reference => reference.ToString("X8")).ToArray()
        }).ToArray();

    private static object ReadScene(byte[] bytes, string archivePath)
    {
        var loader = typeof(SceneActor).Assembly.GetType("JadeView.PcSceneLoader")!;
        var read = loader.GetMethod("Read", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        object replay = read.Invoke(null, [bytes, archivePath])!;
        string? error = (string?)GetField(replay, "Error");
        if (error != null) throw new InvalidDataException(error);
        return replay;
    }

    private static object? GetField(object instance, string name) =>
        instance.GetType().GetField(name)!.GetValue(instance);
}
