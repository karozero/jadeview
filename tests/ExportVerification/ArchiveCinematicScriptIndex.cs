using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using JadeView;

internal static class ArchiveCinematicScriptIndex
{
    private sealed record RecordView(string Container, byte[] Source, uint Key, char Kind,
        int Offset, int Size, uint World, string Name, int BrainOffset, uint EventsKey,
        uint SceneEventsKey, uint[] References);
    private sealed record OwnerObjectReference(string Variable, string Key, string? Name, string Source);
    private sealed record OwnerVariableSnapshot(string Container, string WorldKey, string WorldName,
        string OwnerKey, string OwnerName, string BrainKey, string ModelKey, string EventsKey, string SceneEventsKey,
        CinematicOwnerDefaults.Snapshot? Variables,
        OwnerObjectReference[] ObjectReferences, string? Error);

    private sealed class ScriptUsage(uint key)
    {
        internal uint Key { get; } = key;
        internal HashSet<uint> Functions { get; } = [];
        internal HashSet<string> Owners { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal HashSet<string> Containers { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal HashSet<string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly IReadOnlyDictionary<string, uint[]> PreviewSignatures =
        new Dictionary<string, uint[]>(StringComparer.Ordinal)
        {
            ["environment-cycle"] = [0x5e000004, 0x5e000040],
            ["cinematic-screen-fade"] = [0x7100bd05],
            ["camera-shutter"] = [0x3500b8fe, 0x3500b902],
            ["scripted-shockwave"] = [0x3500c01f],
            ["camera-flash"] = [0xbf002518],
            ["lightning"] = [0x72001350, 0x72001351]
        };

    internal static void Write(string archivePath, string sourceRoot, string output) =>
        Write(archivePath, sourceRoot, output, includeAllWorlds: false);

    internal static void WriteAllWorlds(string archivePath, string sourceRoot, string output) =>
        Write(archivePath, sourceRoot, output, includeAllWorlds: true);

    private static void Write(string archivePath, string sourceRoot, string output, bool includeAllWorlds)
    {
        var archive = new Archive(archivePath);
        var archiveEntries = archive.Entries.GroupBy(entry => entry.Key)
            .ToDictionary(group => group.Key, group => group.Select(entry => new
            {
                entry.Id,
                entry.Name,
                entry.Folder,
                entry.StoredSize
            }).ToArray());
        var fixedEntry = archive.Entries.FirstOrDefault(entry => entry.Id == "FF0084DF");
        var fixedRecords = fixedEntry == null ? [] : ReadRecords(archive.Read(fixedEntry), archive.PathName, fixedEntry.Id);
        var scriptRecordsByKey = new Dictionary<uint, List<RecordView>>();
        foreach (var scriptRecord in fixedRecords.Where(record => record.Kind == 's'))
            Add(scriptRecordsByKey, scriptRecord.Key, scriptRecord);
        var functionNames = ReadFunctionNames(Path.Combine(sourceRoot, "external", "irisEng", "ai", "ai_compiledscripts.c"));
        var usages = new Dictionary<uint, ScriptUsage>();
        var functionOwners = new Dictionary<uint, HashSet<string>>();
        var functionContainers = new Dictionary<uint, HashSet<string>>();
        var missingModels = new Dictionary<uint, HashSet<string>>();
        var ownerVariableSnapshots = new List<OwnerVariableSnapshot>();
        int containerIndex = 0;
        int cinematicContainers = 0;
        int brainOwners = 0;
        int nonCinematicBrainOwners = 0;
        int resolvedOwners = 0;
        int sharedModelOwners = 0;
        int ownersWithMappedFunctions = 0;
        int ownersWithoutMappedFunctions = 0;
        int missingBrainRecords = 0;
        int missingScriptModels = 0;
        var cinematicContainerIds = new List<string>();
        var indexedWorlds = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var sceneEntries = archive.Entries.Where(entry => (entry.Key & 0xfff00000) == 0xff000000)
            .OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase).ToArray();

        foreach (var entry in sceneEntries)
        {
            var sceneRecords = ReadRecords(archive.Read(entry), archive.PathName, entry.Id);
            foreach (var scriptRecord in sceneRecords.Where(record => record.Kind == 's'))
                Add(scriptRecordsByKey, scriptRecord.Key, scriptRecord);
            var cinematicWorldKeys = sceneRecords.Where(record => record.Kind == 'z'
                    && record.Name.Contains("cine", StringComparison.OrdinalIgnoreCase))
                .Select(record => record.Key).ToHashSet();
            var allWorldKeys = sceneRecords.Where(record => record.Kind == 'z')
                .Select(record => record.Key).ToHashSet();
            var worldNamesByKey = sceneRecords.Where(record => record.Kind == 'z')
                .GroupBy(record => record.Key)
                .ToDictionary(group => group.Key, group => group.First().Name);
            var candidateWorlds = includeAllWorlds
                ? sceneRecords.Where(record => record.Kind == 'z')
                : sceneRecords.Where(record => record.Kind == 'z'
                    && cinematicWorldKeys.Contains(record.Key));
            foreach (var world in candidateWorlds)
            {
                var containers = indexedWorlds.GetValueOrDefault(world.Key.ToString("X8"))
                    ?? (indexedWorlds[world.Key.ToString("X8")] = new(StringComparer.OrdinalIgnoreCase));
                containers.Add($"{entry.Id}:{world.Name}");
            }
            if (cinematicWorldKeys.Count > 0)
            {
                cinematicContainers++;
                cinematicContainerIds.Add(entry.Id);
            }
            var roots = sceneRecords.Where(record => record.Kind == 'l' && record.BrainOffset >= 0
                && (includeAllWorlds
                    ? allWorldKeys.Contains(record.World)
                    : cinematicWorldKeys.Contains(record.World)));
            var sceneObjectNames = sceneRecords.Where(record => record.Kind == 'l')
                .GroupBy(record => record.Key)
                .ToDictionary(group => group.Key, group => group.First().Name);
            foreach (var owner in roots)
            {
                brainOwners++;
                string worldName = worldNamesByKey.GetValueOrDefault(owner.World, string.Empty);
                if (!worldName.Contains("cine", StringComparison.OrdinalIgnoreCase))
                    nonCinematicBrainOwners++;
                uint brainKey = ReadUInt32(owner.Source, owner.BrainOffset);
                var resources = fixedRecords.Concat(sceneRecords)
                    .Where(record => record.Container == "FF0084DF" || record.World == 0 || record.World == owner.World)
                    .GroupBy(record => (record.Kind, record.Key))
                    .ToDictionary(group => group.Key, group => group
                        .OrderByDescending(record => record.Container == entry.Id).First());
                if (!resources.TryGetValue(('q', brainKey), out var brain) || brain.Size < sizeof(uint) * 2)
                {
                    missingBrainRecords++;
                    continue;
                }
                uint modelKey = ReadUInt32(brain.Source, brain.Offset);
                if (!resources.TryGetValue(('s', modelKey), out var model))
                {
                    var crossContainerMatches = scriptRecordsByKey.GetValueOrDefault(modelKey, []);
                    var modelVariants = crossContainerMatches.GroupBy(record =>
                        Convert.ToHexString(SHA256.HashData(record.Source.AsSpan(record.Offset, record.Size)))).ToArray();
                    if (modelVariants.Length != 1)
                    {
                        missingScriptModels++;
                        Add(missingModels, modelKey, $"{entry.Id}:{owner.Key:X8}:{owner.Name}");
                        continue;
                    }
                    // Reuse a model only when every archive copy is byte-identical.
                    // Instance overrides remain owner-scoped; shared defaults are separately checked below.
                    model = modelVariants[0].First();
                    sharedModelOwners++;
                }

                CinematicOwnerDefaults.Snapshot? variableSnapshot = null;
                OwnerObjectReference[] objectReferences = [];
                string? variableError = null;
                try
                {
                    var variableClosure = resources.Values
                        .Select(record => new ArchiveFacialScriptCensus.SourceRecord(record.Container,
                            record.Source, record.Key, record.Kind, record.Offset, record.Size, record.World,
                            record.Name, null, record.References))
                        .ToList();
                    AddUniqueModelDefaults(variableClosure, model, sceneRecords, fixedRecords);
                    variableSnapshot = CinematicOwnerDefaults.Read(brainKey, variableClosure);
                    objectReferences = ReadOwnerObjectReferences(variableSnapshot, sceneObjectNames);
                }
                catch (InvalidDataException error)
                {
                    variableError = error.Message;
                }
                ownerVariableSnapshots.Add(new(entry.Id, owner.World.ToString("X8"), worldName,
                    owner.Key.ToString("X8"), owner.Name,
                    brainKey.ToString("X8"), modelKey.ToString("X8"), owner.EventsKey.ToString("X8"),
                    owner.SceneEventsKey.ToString("X8"), variableSnapshot, objectReferences, variableError));

                resolvedOwners++;
                var usage = usages.GetValueOrDefault(modelKey) ?? (usages[modelKey] = new(modelKey));
                string ownerLabel = $"{entry.Id}:{owner.Key:X8}:{owner.Name}";
                usage.Owners.Add(ownerLabel);
                usage.Containers.Add(entry.Id);
                if (!string.IsNullOrWhiteSpace(model.Name)) usage.Names.Add(model.Name);
                uint[] mappedFunctions = model.References.Where(functionNames.ContainsKey).Distinct().ToArray();
                if (mappedFunctions.Length > 0) ownersWithMappedFunctions++;
                else ownersWithoutMappedFunctions++;
                foreach (uint functionKey in mappedFunctions)
                {
                    usage.Functions.Add(functionKey);
                    Add(functionOwners, functionKey, ownerLabel);
                    Add(functionContainers, functionKey, entry.Id);
                }
            }

            containerIndex++;
            if (containerIndex % 10 == 0 || containerIndex == sceneEntries.Length)
                Console.WriteLine($"SCRIPT OWNER INDEX: {containerIndex}/{sceneEntries.Length} containers");
        }

        var scriptModels = usages.Values.OrderByDescending(usage => usage.Owners.Count)
            .ThenBy(usage => usage.Key).Select(usage => new
            {
                Key = usage.Key.ToString("X8"),
                Owners = usage.Owners.Count,
                Containers = usage.Containers.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                Names = usage.Names.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                Functions = usage.Functions.Order().Select(key => new
                {
                    Key = key.ToString("X8"),
                    Name = functionNames[key]
                }).ToArray(),
                PreviewRoles = PreviewSignatures.Where(signature =>
                    signature.Value.All(usage.Functions.Contains)).Select(signature => signature.Key).ToArray()
            }).ToArray();

        var functionSummary = functionOwners.OrderByDescending(pair => pair.Value.Count)
            .ThenBy(pair => pair.Key).Select(pair => new
            {
                Key = pair.Key.ToString("X8"),
                Name = functionNames[pair.Key],
                Owners = pair.Value.Count,
                Containers = functionContainers[pair.Key].Count,
                PreviewRoles = PreviewSignatures.Where(signature => signature.Value.Contains(pair.Key))
                    .Select(signature => signature.Key).ToArray()
            }).ToArray();

        Directory.CreateDirectory(output);
        string reportFileName = includeAllWorlds
            ? "archive-gameplay-script-index.json"
            : "archive-cinematic-script-index.json";
        string destination = Path.Combine(output, reportFileName);
        File.WriteAllText(destination, JsonSerializer.Serialize(new
        {
            Archive = archive.PathName,
            SourceMap = Path.GetRelativePath(sourceRoot, Path.Combine(sourceRoot, "external", "irisEng", "ai", "ai_compiledscripts.c")),
            Containers = sceneEntries.Length,
            Scope = includeAllWorlds ? "all worlds" : "cinematic worlds",
            IndexedWorlds = indexedWorlds.Count,
            IndexedWorldLabels = indexedWorlds.Values.SelectMany(labels => labels)
                .Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            CinematicContainers = cinematicContainers,
            CinematicContainerIds = cinematicContainerIds,
            BrainOwners = brainOwners,
            NonCinematicBrainOwners = nonCinematicBrainOwners,
            ResolvedScriptOwners = resolvedOwners,
            ResolvedByUniqueSharedModelBytes = sharedModelOwners,
            OwnersWithMappedFunctions = ownersWithMappedFunctions,
            OwnersWithoutMappedFunctions = ownersWithoutMappedFunctions,
            MissingBrainRecords = missingBrainRecords,
            MissingScriptModels = missingScriptModels,
            UniqueScriptModels = scriptModels.Length,
            MappedFunctionKeys = functionNames.Count,
            OwnersWithDecodedVariables = ownerVariableSnapshots.Count(snapshot => snapshot.Variables != null),
            OwnersWithVariableErrors = ownerVariableSnapshots.Count(snapshot => snapshot.Error != null),
            OwnerVariables = includeAllWorlds ? null : ownerVariableSnapshots,
            OwnerSummaries = includeAllWorlds ? ownerVariableSnapshots.Select(snapshot => new
            {
                snapshot.Container,
                snapshot.OwnerKey,
                snapshot.OwnerName,
                snapshot.BrainKey,
                snapshot.WorldKey,
                snapshot.WorldName,
                snapshot.ModelKey,
                snapshot.EventsKey,
                snapshot.SceneEventsKey,
                VariableError = snapshot.Error
            }).ToArray() : null,
            Limits = "Brain/model reference census plus initial merged OVA values. Cross-container script models are reused only when copies are byte-identical; a model-default OVA missing from the owner's scoped resource closure is reported as a variable error rather than guessed. Object references remain serialized keys and are not runtime-resolved. Iris function labels are source-map matches, not proof of execution in this scene. Jaded is a later engine source and is not used as a retail-PC function map. Owner/action dispatch, script conditions and runtime state are not executed.",
            PreviewSignatures = PreviewSignatures.Select(signature => new
            { Role = signature.Key, FunctionKeys = signature.Value.Select(key => key.ToString("X8")).ToArray() }).ToArray(),
            TopFunctions = functionSummary.Take(300).ToArray(),
            MissingScriptModelKeys = missingModels.OrderByDescending(pair => pair.Value.Count)
                .ThenBy(pair => pair.Key).Select(pair => new
                {
                    Key = pair.Key.ToString("X8"),
                    Owners = pair.Value.Count,
                    Examples = pair.Value.Take(8).ToArray(),
                    ArchiveEntries = archiveEntries.GetValueOrDefault(pair.Key) ?? [],
                    OtherContainerModels = scriptRecordsByKey.GetValueOrDefault(pair.Key, [])
                        .Select(record => new
                        {
                            record.Container,
                            World = record.World.ToString("X8"),
                            Sha256 = Convert.ToHexString(SHA256.HashData(record.Source.AsSpan(record.Offset, record.Size)))
                        }).ToArray()
                }).ToArray(),
            ScriptModels = scriptModels
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"SCRIPT OWNER INDEX: {brainOwners} brain owners, {resolvedOwners} resolved, {scriptModels.Length} script models; {destination}");
    }

    private static Dictionary<uint, string> ReadFunctionNames(string sourcePath)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("IrisEngine callback map is required for this audit.", sourcePath);
        var functionNames = new Dictionary<uint, string>();
        var pattern = new Regex(@"\{\s*0x([0-9a-fA-F]{1,8})\s*,\s*([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
        foreach (Match match in pattern.Matches(File.ReadAllText(sourcePath)))
            functionNames[Convert.ToUInt32(match.Groups[1].Value, 16)] = match.Groups[2].Value;
        return functionNames;
    }

    private static List<RecordView> ReadRecords(byte[] bytes, string archivePath, string container)
    {
        var loader = typeof(SceneActor).Assembly.GetType("JadeView.PcSceneLoader")!;
        var read = loader.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)!;
        object replay = read.Invoke(null, [bytes, archivePath])!;
        string? error = (string?)GetField(replay, "Error");
        if (error != null) throw new InvalidDataException($"{container}: {error}");
        var result = new List<RecordView>();
        foreach (object record in (IEnumerable)GetField(replay, "Records")!)
        {
            int offset = (int)GetField(record, "Offset")!;
            int size = (int)GetField(record, "Size")!;
            int brainOffset = (int)GetField(record, "BrainFieldOffset")!;
            uint eventsKey = (uint)GetField(record, "Events")!;
            uint sceneEventsKey = (uint)GetField(record, "SceneEvents")!;
            var references = ((IEnumerable)GetField(record, "References")!).Cast<object>()
                .Select(reference => (uint)reference.GetType().GetProperty("Key")!.GetValue(reference)!).ToArray();
            result.Add(new(container, bytes, (uint)GetField(record, "Key")!, (char)GetField(record, "Kind")!,
                offset, size, (uint)GetField(record, "LoadWorldKey")!, (string)GetField(record, "Name")!,
                brainOffset, eventsKey, sceneEventsKey, references));
        }
        return result;
    }

    private static object? GetField(object instance, string name) =>
        instance.GetType().GetField(name)!.GetValue(instance);

    private static uint ReadUInt32(byte[] source, int offset) => BitConverter.ToUInt32(source, offset);

    private static OwnerObjectReference[] ReadOwnerObjectReferences(CinematicOwnerDefaults.Snapshot snapshot,
        IReadOnlyDictionary<uint, string> sceneObjectNames) => snapshot.Variables
        .Where(variable => variable.Type == 40)
        .SelectMany(variable => ReadVariableObjectKeys(variable)
            .Where(key => key is not 0 and not uint.MaxValue)
            .Select(key => new OwnerObjectReference(variable.Name, key.ToString("X8"),
                sceneObjectNames.GetValueOrDefault(key), variable.Source)))
        .Distinct()
        .ToArray();

    private static void AddUniqueModelDefaults(
        List<ArchiveFacialScriptCensus.SourceRecord> closure, RecordView model,
        IReadOnlyList<RecordView> sceneRecords, IReadOnlyList<RecordView> fixedRecords)
    {
        if (!closure.Any(record => record.Kind == 's' && record.Key == model.Key))
            closure.Add(ToSourceRecord(model));

        foreach (uint variablesKey in ReadModelVariablesKeys(model))
        {
            if (closure.Any(record => record.Kind == 't' && record.Key == variablesKey)) continue;
            var candidates = sceneRecords.Concat(fixedRecords)
                .Where(record => record.Kind == 't' && record.Key == variablesKey)
                .GroupBy(record => Convert.ToHexString(record.Source.AsSpan(record.Offset, record.Size)))
                .ToArray();
            if (candidates.Length == 1)
                closure.Add(ToSourceRecord(candidates[0].First()));
        }
    }

    private static uint[] ReadModelVariablesKeys(RecordView model)
    {
        var keys = new List<uint>();
        for (int position = model.Offset; position + 8 <= model.Offset + model.Size; position += 8)
        {
            if (!model.Source.AsSpan(position + 4, 4).SequenceEqual(".ova"u8)) continue;
            uint key = ReadUInt32(model.Source, position);
            if (key is not 0 and not uint.MaxValue) keys.Add(key);
        }
        return keys.Distinct().ToArray();
    }

    private static ArchiveFacialScriptCensus.SourceRecord ToSourceRecord(RecordView record) =>
        new(record.Container, record.Source, record.Key, record.Kind, record.Offset, record.Size,
            record.World, record.Name, null, record.References);

    private static IEnumerable<uint> ReadVariableObjectKeys(CinematicOwnerDefaults.VariableSnapshot variable)
    {
        byte[] rawBytes = Convert.FromHexString(variable.ValueBytes);
        uint elementCount = variable.PackedLength & 0x3fffffff;
        int prefixBytes = variable.PackedLength == 1
            ? 0 : checked((int)(variable.PackedLength >> 30) * sizeof(uint));
        for (int index = 0; index < elementCount; index++)
        {
            int offset = checked(prefixBytes + index * sizeof(uint));
            if (offset > rawBytes.Length - sizeof(uint)) yield break;
            yield return BitConverter.ToUInt32(rawBytes, offset);
        }
    }

    private static void Add(Dictionary<uint, HashSet<string>> index, uint key, string value)
    {
        if (!index.TryGetValue(key, out var values)) index.Add(key, values = new(StringComparer.OrdinalIgnoreCase));
        values.Add(value);
    }

    private static void Add(Dictionary<uint, List<RecordView>> index, uint key, RecordView value)
    {
        if (!index.TryGetValue(key, out var values)) index.Add(key, values = []);
        values.Add(value);
    }
}
