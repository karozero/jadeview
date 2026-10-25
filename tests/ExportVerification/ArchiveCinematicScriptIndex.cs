using System.Collections;
using System.Buffers.Binary;
using System.Globalization;
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
        OwnerObjectReference[] ObjectReferences, CinematicScriptCallbackSlot[]? CallbackSlots, string? Error)
    {
        internal EventFlagAccess[] EventFlagAccesses { get; init; } = [];
    }
    private sealed record SourceFlagAccessOwner(string Container, string WorldKey, string WorldName,
        string OwnerKey, string OwnerName, string ModelKey, string[] CallbackNames,
        CinematicScriptFlagAccess[] Accesses, EventFlagAccess[] EventAccesses);
    private sealed record EventFlagAccess(string LinkName, string EventKey, string EventName,
        int TrackIndex, float Time, string TargetKey, int Flag, string Access, string Function,
        bool? WrittenSet, string Description);
    private sealed record OwnerTrackMutation(string FunctionKey, string FunctionName,
        int NodeIndex, string Operation, int? TrackIndex, string? TargetKey,
        string? TargetName, string Status);
    private sealed record TrackMutationOwner(string Container, string WorldKey, string WorldName,
        string OwnerKey, string OwnerName, string BrainKey, string ModelKey,
        OwnerTrackMutation[] Mutations);
    private sealed record SerializedFunctionPayload(PcSerializedAiFunction? Function, string Status);
    private sealed class AiFunctionPayloadCoverage
    {
        internal int ModelFunctionReferences;
        internal int ResolvedFunctionResources;
        internal int MissingFunctionResources;
        internal int FullSerializedBodies;
        internal int CompactMetadataOnly;
        internal int UnsupportedPayloads;
    }
    private sealed record UnmappedCallbackExample(string Container, string WorldKey, string WorldName,
        string OwnerKey, string OwnerName, string BrainKey, string ModelKey, int SlotIndex);
    private sealed record EngineCallbackAssignment(string Container, string WorldKey, string WorldName,
        string OwnerKey, string OwnerName, string BrainKey, string ModelKey, int EventId,
        uint ModelOvaKey, uint FunctionKey, string? FunctionName, bool UniqueModelOvaBinding);

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

    internal static bool VerifyOwnerResourceResolution()
    {
        byte[] firstPayload = [1, 2, 3, 4];
        byte[] conflictingPayload = [4, 3, 2, 1];
        var preferredFirst = new RecordView("FF001000", firstPayload, 0xA100, 'q', 0, 4,
            0xB100, "first", -1, uint.MaxValue, uint.MaxValue, []);
        var preferredConflict = new RecordView("FF001000", conflictingPayload, 0xA100, 'q', 0, 4,
            0xB100, "conflict", -1, uint.MaxValue, uint.MaxValue, []);
        Dictionary<(char Kind, uint Key), RecordView> ambiguousResult = ResolveOwnerResources(
            [], [preferredFirst, preferredConflict], "FF001000", 0xB100, out var ambiguousKeys);

        var identicalSharedCopy = new RecordView("FF002000", firstPayload, 0xA200, 's', 0, 4,
            0, "shared", -1, uint.MaxValue, uint.MaxValue, []);
        Dictionary<(char Kind, uint Key), RecordView> sharedResult = ResolveOwnerResources(
            [identicalSharedCopy], [], "FF003000", 0xB100, out var sharedAmbiguities);
        return !ambiguousResult.ContainsKey(('q', 0xA100))
            && ambiguousKeys.Contains(('q', 0xA100))
            && sharedResult.ContainsKey(('s', 0xA200))
            && sharedAmbiguities.Count == 0
            && VerifyEngineCallbackCandidates();
    }

    private static bool VerifyEngineCallbackCandidates()
    {
        const uint firstOvaKey = 0xA300;
        const uint secondOvaKey = 0xA301;
        const uint callbackKey = 0xB300;
        var functionNames = new Dictionary<uint, string> { [callbackKey] = "test_after_record" };

        RecordView MakeModel(params (uint Key, string Type)[] references)
        {
            byte[] modelBytes = new byte[references.Length * 8];
            for (int index = 0; index < references.Length; index++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(modelBytes.AsSpan(index * 8), references[index].Key);
                System.Text.Encoding.ASCII.GetBytes(references[index].Type, modelBytes.AsSpan(index * 8 + 4, 4));
            }
            return new("FF001000", modelBytes, 0xA200, 's', 0, modelBytes.Length,
                0xA100, "model", -1, uint.MaxValue, uint.MaxValue, []);
        }

        RecordView MakeOva(uint key, params uint[] callbacks)
        {
            byte[] payload = new byte[12 + 5 * sizeof(uint) + sizeof(int) + callbacks.Length * sizeof(uint)];
            int countOffset = 12 + 5 * sizeof(uint);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(countOffset), callbacks.Length);
            for (int index = 0; index < callbacks.Length; index++)
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(countOffset + sizeof(int) + index * sizeof(uint)), callbacks[index]);
            return new("FF001000", payload, key, 't', 0, payload.Length,
                0xA100, $"OVA {key:X8}", -1, uint.MaxValue, uint.MaxValue, []);
        }

        RecordView uniqueModel = MakeModel((firstOvaKey, ".ova"), (0xA400, ".ofc"));
        var uniqueResources = new Dictionary<(char Kind, uint Key), RecordView>
        {
            [('t', firstOvaKey)] = MakeOva(firstOvaKey, callbackKey)
        };
        var uniqueCandidates = ReadEngineCallbackCandidates(uniqueModel, uniqueResources, functionNames);
        bool uniqueBindingPreservesTypedProvenance = uniqueCandidates.Length == 1
            && uniqueCandidates[0].OvaKey == firstOvaKey
            && uniqueCandidates[0].Slot.EventId == 0
            && uniqueCandidates[0].Slot.FunctionKey == callbackKey
            && uniqueCandidates[0].Slot.FunctionName == "test_after_record";

        RecordView ambiguousModel = MakeModel((firstOvaKey, ".ova"), (secondOvaKey, ".ova"));
        var ambiguousResources = new Dictionary<(char Kind, uint Key), RecordView>
        {
            [('t', firstOvaKey)] = MakeOva(firstOvaKey, callbackKey),
            [('t', secondOvaKey)] = MakeOva(secondOvaKey, callbackKey)
        };
        var ambiguousCandidates = ReadEngineCallbackCandidates(ambiguousModel, ambiguousResources, functionNames);
        bool multipleOvaAssignmentsRemainAmbiguous = ambiguousCandidates.Length == 2
            && ambiguousCandidates.Select(candidate => candidate.OvaKey).Distinct().Count() == 2;

        return uniqueBindingPreservesTypedProvenance && multipleOvaAssignmentsRemainAmbiguous;
    }

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
        var namedResourceCandidates = new Dictionary<uint, HashSet<string>>();
        AddNamedResourceCandidates(namedResourceCandidates, fixedRecords);
        var scriptRecordsByKey = new Dictionary<uint, List<RecordView>>();
        foreach (var scriptRecord in fixedRecords.Where(record => record.Kind == 's'))
            Add(scriptRecordsByKey, scriptRecord.Key, scriptRecord);
        var functionNames = ReadFunctionNames(Path.Combine(sourceRoot, "external", "irisEng", "ai", "ai_compiledscripts.c"));
        var usages = new Dictionary<uint, ScriptUsage>();
        var functionOwners = new Dictionary<uint, HashSet<string>>();
        var functionContainers = new Dictionary<uint, HashSet<string>>();
        var missingModels = new Dictionary<uint, HashSet<string>>();
        var ownerVariableSnapshots = new List<OwnerVariableSnapshot>();
        var trackMutationOwners = new List<TrackMutationOwner>();
        var parsedFunctionsByPayload = new Dictionary<string, SerializedFunctionPayload>(StringComparer.Ordinal);
        var aiFunctionPayloadCoverage = new AiFunctionPayloadCoverage();
        var decodedEventClips = new Dictionary<(string Container, uint Key), AnimationClip>();
        var unmappedCallbackOwners = new Dictionary<uint, List<UnmappedCallbackExample>>();
        var engineCallbackAssignments = new List<EngineCallbackAssignment>();
        int containerIndex = 0;
        int cinematicContainers = 0;
        int brainOwners = 0;
        int nonCinematicBrainOwners = 0;
        int resolvedOwners = 0;
        int sharedModelOwners = 0;
        int ownersWithMappedFunctions = 0;
        int ownersWithoutMappedFunctions = 0;
        int ownersWithDecodedCallbackSlots = 0;
        int ownersWithUnmappedCallbacks = 0;
        int configuredCallbackSlots = 0;
        int unmappedCallbackSlots = 0;
        int missingBrainRecords = 0;
        int missingScriptModels = 0;
        int ambiguousBrainRecords = 0;
        int ambiguousScriptModels = 0;
        var cinematicContainerIds = new List<string>();
        var indexedWorlds = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var sceneEntries = archive.Entries.Where(entry => (entry.Key & 0xfff00000) == 0xff000000)
            .OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase).ToArray();

        foreach (var entry in sceneEntries)
        {
            var sceneRecords = ReadRecords(archive.Read(entry), archive.PathName, entry.Id);
            AddNamedResourceCandidates(namedResourceCandidates, sceneRecords);
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
                .GroupBy(record => (record.World, record.Key))
                .Select(group => new
                {
                    group.Key,
                    Names = group.Select(record => record.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                })
                .Where(group => group.Names.Length == 1)
                .ToDictionary(group => group.Key, group => group.Names[0]);
            foreach (var owner in roots)
            {
                brainOwners++;
                string worldName = worldNamesByKey.GetValueOrDefault(owner.World, string.Empty);
                if (!worldName.Contains("cine", StringComparison.OrdinalIgnoreCase))
                    nonCinematicBrainOwners++;
                uint brainKey = ReadUInt32(owner.Source, owner.BrainOffset);
                var resources = ResolveOwnerResources(fixedRecords, sceneRecords, entry.Id, owner.World,
                    out var ambiguousResourceKeys);
                if (!resources.TryGetValue(('q', brainKey), out var brain) || brain.Size < sizeof(uint) * 2)
                {
                    missingBrainRecords++;
                    if (ambiguousResourceKeys.Contains(('q', brainKey))) ambiguousBrainRecords++;
                    continue;
                }
                uint modelKey = ReadUInt32(brain.Source, brain.Offset);
                if (!resources.TryGetValue(('s', modelKey), out var model))
                {
                    if (ambiguousResourceKeys.Contains(('s', modelKey)))
                    {
                        ambiguousScriptModels++;
                        missingScriptModels++;
                        Add(missingModels, modelKey, $"{entry.Id}:{owner.Key:X8}:{owner.Name}");
                        continue;
                    }
                    var crossContainerMatches = scriptRecordsByKey.GetValueOrDefault(modelKey, []);
                    var modelVariants = crossContainerMatches.GroupBy(record =>
                        Convert.ToHexString(SHA256.HashData(record.Source.AsSpan(record.Offset, record.Size)))).ToArray();
                    if (modelVariants.Length != 1)
                    {
                        missingScriptModels++;
                        if (modelVariants.Length > 1) ambiguousScriptModels++;
                        Add(missingModels, modelKey, $"{entry.Id}:{owner.Key:X8}:{owner.Name}");
                        continue;
                    }
                    // Reuse a model only when every archive copy is byte-identical.
                    // Instance overrides remain owner-scoped; shared defaults are separately checked below.
                    model = modelVariants[0].First();
                    sharedModelOwners++;
                }

                var variableClosure = resources.Values
                    .Select(record => new ArchiveFacialScriptCensus.SourceRecord(record.Container,
                        record.Source, record.Key, record.Kind, record.Offset, record.Size, record.World,
                        record.Name, null, record.References))
                    .ToList();
                AddUniqueModelDefaults(variableClosure, model, sceneRecords, fixedRecords);
                CinematicOwnerDefaults.Snapshot? variableSnapshot = null;
                OwnerObjectReference[] objectReferences = [];
                string? variableError = null;
                try
                {
                    variableSnapshot = CinematicOwnerDefaults.Read(brainKey, variableClosure);
                    objectReferences = ReadOwnerObjectReferences(variableSnapshot, owner.World, sceneObjectNames);
                }
                catch (InvalidDataException error)
                {
                    variableError = error.Message;
                }
                CinematicScriptCallbackSlot[]? callbackSlots = null;
                try
                {
                    callbackSlots = CinematicOwnerDefaults.ReadCallbackSlots(brainKey, variableClosure,
                        functionNames);
                }
                catch (InvalidDataException)
                {
                    // Keep callback-slot coverage separate from variable-layout diagnostics.
                }
                (uint OvaKey, CinematicScriptEngineCallbackSlot Slot)[] engineCallbackCandidates =
                    ReadEngineCallbackCandidates(model, resources, functionNames);
                bool uniqueModelOvaBinding = engineCallbackCandidates
                    .Select(candidate => candidate.OvaKey).Distinct().Count() == 1;
                foreach ((uint ovaKey, CinematicScriptEngineCallbackSlot callback) in engineCallbackCandidates)
                {
                    if (callback.FunctionKey == 0) continue;
                    engineCallbackAssignments.Add(new(entry.Id, owner.World.ToString("X8"), worldName,
                        owner.Key.ToString("X8"), owner.Name, brainKey.ToString("X8"),
                        modelKey.ToString("X8"), callback.EventId, ovaKey, callback.FunctionKey,
                        callback.FunctionName, uniqueModelOvaBinding));
                }
                OwnerTrackMutation[] ownerTrackMutations = ReadOwnerTrackMutations(model, resources,
                    functionNames, parsedFunctionsByPayload, aiFunctionPayloadCoverage);
                if (ownerTrackMutations.Length > 0)
                {
                    trackMutationOwners.Add(new(entry.Id, owner.World.ToString("X8"), worldName,
                        owner.Key.ToString("X8"), owner.Name, brainKey.ToString("X8"),
                        modelKey.ToString("X8"), ownerTrackMutations));
                }
                if (callbackSlots != null)
                {
                    ownersWithDecodedCallbackSlots++;
                    var ownerHasUnmappedCallback = false;
                    foreach (var slot in callbackSlots.Where(slot => slot.FunctionKey != 0))
                    {
                        configuredCallbackSlots++;
                        if (slot.FunctionName != null) continue;
                        ownerHasUnmappedCallback = true;
                        unmappedCallbackSlots++;
                        if (!unmappedCallbackOwners.TryGetValue(slot.FunctionKey, out var examples))
                            unmappedCallbackOwners.Add(slot.FunctionKey, examples = []);
                        examples.Add(new(entry.Id, owner.World.ToString("X8"), worldName,
                            owner.Key.ToString("X8"), owner.Name, brainKey.ToString("X8"),
                            modelKey.ToString("X8"), slot.Index));
                    }
                    if (ownerHasUnmappedCallback) ownersWithUnmappedCallbacks++;
                }
                ownerVariableSnapshots.Add(new(entry.Id, owner.World.ToString("X8"), worldName,
                    owner.Key.ToString("X8"), owner.Name,
                    brainKey.ToString("X8"), modelKey.ToString("X8"), owner.EventsKey.ToString("X8"),
                    owner.SceneEventsKey.ToString("X8"), variableSnapshot, objectReferences, callbackSlots,
                    variableError));

                // Owner-linked EVE resources carry serialized event commands. Decode each resource
                // once, then retain only global saved-flag accesses for the archive-wide graph.
                var linkedEventFlagAccesses = ReadLinkedEventFlagAccesses(owner, resources,
                    decodedEventClips);
                if (linkedEventFlagAccesses.Length > 0)
                {
                    int snapshotIndex = ownerVariableSnapshots.Count - 1;
                    ownerVariableSnapshots[snapshotIndex] = ownerVariableSnapshots[snapshotIndex] with
                    {
                        EventFlagAccesses = linkedEventFlagAccesses
                    };
                }

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

        var scriptOwnerCandidates = ownerVariableSnapshots.Select(snapshot =>
            new CinematicScriptOwnerReferenceCandidate(snapshot.Container,
                uint.Parse(snapshot.WorldKey, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                uint.Parse(snapshot.OwnerKey, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                snapshot.OwnerName)).ToArray();
        var ownerReferenceResolutions = ownerVariableSnapshots.SelectMany(source => source.ObjectReferences
            .Select(reference =>
            {
                uint targetKey = uint.Parse(reference.Key, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                uint sourceWorldKey = uint.Parse(source.WorldKey, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                var resolution = CinematicScriptOwnerReferenceResolver.Resolve(source.Container,
                    sourceWorldKey, targetKey, scriptOwnerCandidates);
                return new { Source = source, Reference = reference, Resolution = resolution };
            })
            .Where(item => item.Resolution.Candidates.Length > 0))
            .ToArray();
        var referencesToScriptOwners = ownerReferenceResolutions
            .Where(item => item.Resolution.IsResolved)
            .Select(item => new
            {
                item.Source.Container,
                item.Source.WorldKey,
                item.Source.WorldName,
                item.Source.OwnerKey,
                item.Source.OwnerName,
                Reference = item.Reference.Variable,
                item.Reference.Source,
                item.Reference.Key,
                TargetName = item.Resolution.Owner!.OwnerName,
                TargetWorldKey = item.Resolution.Owner.WorldKey.ToString("X8"),
                TargetContainer = item.Resolution.Owner.Container,
                item.Resolution.Status
            })
            .OrderBy(link => link.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(link => link.Container, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var unresolvedReferencesToScriptOwners = ownerReferenceResolutions
            .Where(item => !item.Resolution.IsResolved)
            .Select(item => new
            {
                item.Source.Container,
                item.Source.WorldKey,
                item.Source.WorldName,
                item.Source.OwnerKey,
                item.Source.OwnerName,
                Reference = item.Reference.Variable,
                item.Reference.Source,
                item.Reference.Key,
                item.Resolution.Status,
                Candidates = item.Resolution.Candidates.Select(candidate => new
                {
                    candidate.Container,
                    WorldKey = candidate.WorldKey.ToString("X8"),
                    OwnerKey = candidate.OwnerKey.ToString("X8"),
                    candidate.OwnerName
                }).ToArray()
            })
            .OrderBy(link => link.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(link => link.Container, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var callbacksByModel = usages.ToDictionary(pair => pair.Key,
            pair => pair.Value.Functions.Select(functionKey => functionNames[functionKey])
                .ToHashSet(StringComparer.Ordinal));
        var sourceFlagAccessOwners = includeAllWorlds
            ? ownerVariableSnapshots.Select(snapshot => BuildSourceFlagAccessOwner(snapshot, callbacksByModel))
                .Where(owner => owner != null).Select(owner => owner!).ToArray()
            : [];
        var sourceFlagGroups = sourceFlagAccessOwners
            .SelectMany(owner => owner.Accesses.Select(access => new
                { Owner = owner, Access = access, Event = (EventFlagAccess?)null }))
            .Concat(sourceFlagAccessOwners.SelectMany(owner => owner.EventAccesses.Select(access => new
                { Owner = owner, Access = new CinematicScriptFlagAccess(access.Flag, access.Access,
                    access.Function, access.Description, WrittenSet: access.WrittenSet), Event = (EventFlagAccess?)access })))
            .GroupBy(row => row.Access.Flag)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var accesses = group.ToArray();
                var readers = accesses.Where(row => row.Access.Access == "reads").ToArray();
                var writers = accesses.Where(row => row.Access.Access == "writes").ToArray();
                var links = writers.SelectMany(writer => readers
                        .Where(reader => reader.Owner.Container != writer.Owner.Container
                            || reader.Owner.WorldKey != writer.Owner.WorldKey
                            || reader.Owner.OwnerKey != writer.Owner.OwnerKey)
                        .Select(reader => new { Writer = writer, Reader = reader }))
                    .DistinctBy(link => (link.Writer.Owner.Container, link.Writer.Owner.WorldKey,
                        link.Writer.Owner.OwnerKey, link.Reader.Owner.Container, link.Reader.Owner.WorldKey,
                        link.Reader.Owner.OwnerKey))
                    .ToArray();
                return new
                {
                    Flag = group.Key,
                    PotentialLinkCount = links.Length,
                    LinksTruncated = links.Length > 64,
                    Readers = readers.Select(row => new
                    {
                        row.Owner.Container,
                        row.Owner.WorldKey,
                        row.Owner.WorldName,
                        row.Owner.OwnerKey,
                        row.Owner.OwnerName,
                        row.Owner.ModelKey,
                        row.Owner.CallbackNames,
                        row.Access.VariableName,
                        row.Access.RequiredSet,
                        row.Access.Description,
                        Event = row.Event
                    }).ToArray(),
                    Writers = writers.Select(row => new
                    {
                        row.Owner.Container,
                        row.Owner.WorldKey,
                        row.Owner.WorldName,
                        row.Owner.OwnerKey,
                        row.Owner.OwnerName,
                        row.Owner.ModelKey,
                        row.Owner.CallbackNames,
                        row.Access.VariableName,
                        row.Access.WrittenSet,
                        row.Access.Description,
                        Event = row.Event
                    }).ToArray(),
                    Links = links.Take(64).Select(link =>
                    {
                        var writer = link.Writer;
                        var reader = link.Reader;
                        return new
                        {
                            Writer = new
                            {
                                writer.Owner.Container,
                                writer.Owner.WorldKey,
                                writer.Owner.OwnerKey,
                                writer.Owner.OwnerName,
                                writer.Access.VariableName,
                                writer.Access.WrittenSet,
                                writer.Event
                            },
                            Reader = new
                            {
                                reader.Owner.Container,
                                reader.Owner.WorldKey,
                                reader.Owner.OwnerKey,
                                reader.Owner.OwnerName,
                                reader.Access.VariableName,
                                reader.Access.RequiredSet,
                                reader.Event
                            },
                            StateRelation = DescribeFlagStateRelation(writer.Access.WrittenSet,
                                reader.Access.RequiredSet)
                        };
                    }).ToArray()
                };
            }).ToArray();
        var cineEventsOwners = includeAllWorlds
            ? ownerVariableSnapshots.Where(snapshot => callbacksByModel
                    .GetValueOrDefault(Convert.ToUInt32(snapshot.ModelKey, 16))?
                    .Contains("Cine_Events_cine_tests_depart") == true)
                .Select(snapshot => BuildCineEventsSummary(snapshot))
                .ToArray()
            : null;
        var messageGateOwners = includeAllWorlds
            ? ownerVariableSnapshots.Where(snapshot => HasConfiguredMessageGate(snapshot, callbacksByModel))
                .Select(snapshot => BuildMessageGateSummary(snapshot, callbacksByModel))
                .ToArray()
            : null;

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
            OwnersWithDecodedCallbackSlots = ownersWithDecodedCallbackSlots,
            OwnersWithoutDecodedCallbackSlots = resolvedOwners - ownersWithDecodedCallbackSlots,
            OwnersWithUnmappedCallbacks = ownersWithUnmappedCallbacks,
            ConfiguredCallbackSlots = configuredCallbackSlots,
            EngineCallbackOwners = engineCallbackAssignments
                .DistinctBy(assignment => (assignment.Container, assignment.WorldKey, assignment.OwnerKey))
                .Count(),
            EngineCallbackAssignmentCandidates = engineCallbackAssignments.Count,
            UniquelyBoundEngineCallbackAssignments = engineCallbackAssignments
                .Count(assignment => assignment.UniqueModelOvaBinding),
            AmbiguousEngineCallbackAssignmentCandidates = engineCallbackAssignments
                .Count(assignment => !assignment.UniqueModelOvaBinding),
            EngineCallbackIds = engineCallbackAssignments.GroupBy(assignment => assignment.EventId)
                .OrderBy(group => group.Key).Select(group => new
                {
                    EventId = group.Key,
                    Event = CinematicEngineCallbackCatalog.Describe(group.Key),
                    IrisCallSiteConfirmed = CinematicEngineCallbackCatalog.IsConfirmedByIrisCallSite(group.Key),
                    AssignmentCandidates = group.Count(),
                    UniquelyBoundAssignments = group.Count(assignment => assignment.UniqueModelOvaBinding),
                    AmbiguousCandidates = group.Count(assignment => !assignment.UniqueModelOvaBinding),
                    CandidateOwners = group.Select(assignment => (assignment.Container, assignment.WorldKey,
                        assignment.OwnerKey)).Distinct().Count(),
                    Containers = group.Select(assignment => assignment.Container)
                        .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    Functions = group.Select(assignment => assignment.FunctionName
                            ?? $"{assignment.FunctionKey:X8}")
                        .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)
                        .Take(24).ToArray(),
                    Examples = group.Take(12).Select(assignment => new
                    {
                        assignment.Container,
                        assignment.WorldKey,
                        assignment.WorldName,
                        assignment.OwnerKey,
                        assignment.OwnerName,
                        ModelOvaKey = assignment.ModelOvaKey.ToString("X8"),
                        FunctionKey = assignment.FunctionKey.ToString("X8"),
                        assignment.FunctionName,
                        assignment.UniqueModelOvaBinding
                    }).ToArray()
                }).ToArray(),
            UnmappedCallbackSlots = unmappedCallbackSlots,
            UnmappedCallbackKeys = unmappedCallbackOwners.OrderByDescending(pair => pair.Value.Count)
                .ThenBy(pair => pair.Key).Select(pair => new
                {
                    Key = pair.Key.ToString("X8"),
                    Slots = pair.Value.Count,
                    Owners = pair.Value.DistinctBy(example => (example.Container, example.OwnerKey)).Count(),
                    Examples = pair.Value.DistinctBy(example => (example.Container, example.OwnerKey))
                        .Take(8).Select(example =>
                            $"{example.Container}:{example.WorldKey}:{example.OwnerKey}:{example.OwnerName}").ToArray(),
                    SlotDetails = pair.Value.Take(16).Select(example => new
                    {
                        example.Container,
                        example.WorldKey,
                        example.WorldName,
                        example.OwnerKey,
                        example.OwnerName,
                        example.BrainKey,
                        example.ModelKey,
                        CallbackSlot = example.SlotIndex,
                        FunctionKey = pair.Key.ToString("X8"),
                        CandidateResources = namedResourceCandidates.GetValueOrDefault(pair.Key)?
                            .Order(StringComparer.OrdinalIgnoreCase).ToArray() ?? []
                    }).ToArray()
                }).ToArray(),
            MissingBrainRecords = missingBrainRecords,
            AmbiguousBrainRecords = ambiguousBrainRecords,
            MissingScriptModels = missingScriptModels,
            AmbiguousScriptModels = ambiguousScriptModels,
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
            ReferencesToScriptOwners = includeAllWorlds ? referencesToScriptOwners : null,
            UnresolvedReferencesToScriptOwners = includeAllWorlds ? unresolvedReferencesToScriptOwners : null,
            SourceFlagAccessOwners = includeAllWorlds ? sourceFlagAccessOwners : null,
            SourceFlagGroups = includeAllWorlds ? sourceFlagGroups : null,
            EventFlagAccessCount = includeAllWorlds
                ? sourceFlagAccessOwners.Sum(owner => owner.EventAccesses.Length) : (int?)null,
            CineEventsOwners = cineEventsOwners,
            MessageGateOwners = messageGateOwners,
            AiFunctionPayloadCoverage = new
            {
                aiFunctionPayloadCoverage.ModelFunctionReferences,
                aiFunctionPayloadCoverage.ResolvedFunctionResources,
                aiFunctionPayloadCoverage.MissingFunctionResources,
                UniquePayloads = parsedFunctionsByPayload.Count,
                aiFunctionPayloadCoverage.FullSerializedBodies,
                aiFunctionPayloadCoverage.CompactMetadataOnly,
                aiFunctionPayloadCoverage.UnsupportedPayloads
            },
            TrackMutationOwnerCount = trackMutationOwners.Count,
            TrackMutationCandidateCount = trackMutationOwners.Sum(owner => owner.Mutations.Length),
            TrackMutationOwnersTruncated = trackMutationOwners.Count > 1000,
            TrackMutationOwners = trackMutationOwners.Take(1000).ToArray(),
            Limits = "Brain/model reference census plus initial merged OVA values, callback-analyzer accesses, direct Iris ARRGlobalIntFlag* calls, ARRBitTest/Set/Clear calls only when the source buffer resolves to Flag_speciaux_sauve, bounded ARRBitSetRange/ARRBitClearRange calls only when buffer and inclusive endpoints resolve within the 4488-byte Iris buffer, and literal saved-flag commands in owner-linked Events/SceneEvents EVE records. Owner-to-owner edge counts are complete for the recognized accesses, with up to 64 sample links per flag. Unknown saved-buffer aliases, dynamic/reversed/out-of-bounds ranges and unrelated bitsets are not decoded as saved flags. Cross-container script models are reused only when copies are byte-identical; a model-default OVA missing from the owner's scoped resource closure is reported as a variable error rather than guessed. OVA object-reference values remain serialized keys; owner-reference edges use container/world precedence and preserve ambiguous candidates, but do not prove runtime binding. Iris function labels are source-map matches, not proof of execution in this scene. Jaded is a later engine source and is not used as a retail-PC function map. Owner/action dispatch, script conditions and runtime state are not executed.",
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

    private static (uint OvaKey, CinematicScriptEngineCallbackSlot Slot)[] ReadEngineCallbackCandidates(RecordView model,
        IReadOnlyDictionary<(char Kind, uint Key), RecordView> resources,
        IReadOnlyDictionary<uint, string> functionNames)
    {
        var modelOvaKeys = new HashSet<uint>();
        for (int offset = 0; offset <= model.Size - 8; offset += 8)
        {
            ReadOnlySpan<byte> typeTag = model.Source.AsSpan(model.Offset + offset + sizeof(uint), sizeof(uint));
            if (typeTag.SequenceEqual(".ova"u8))
                modelOvaKeys.Add(ReadUInt32(model.Source, model.Offset + offset));
        }

        var candidates = new List<(uint OvaKey, CinematicScriptEngineCallbackSlot Slot)>();
        foreach (uint ovaKey in modelOvaKeys)
        {
            if (!resources.TryGetValue(('t', ovaKey), out RecordView? variableRecord)) continue;
            CinematicScriptEngineCallbackSlot[]? slots = PcScriptEngineCallbackReader.Read(
                variableRecord.Source.AsSpan(variableRecord.Offset, variableRecord.Size), functionNames);
            if (slots == null) continue;
            candidates.AddRange(slots.Where(slot => slot.FunctionKey != 0)
                .Select(slot => (ovaKey, slot)));
        }

        return candidates.ToArray();
    }

    private static OwnerTrackMutation[] ReadOwnerTrackMutations(RecordView model,
        IReadOnlyDictionary<(char Kind, uint Key), RecordView> resources,
        IReadOnlyDictionary<uint, string> functionNames,
        IDictionary<string, SerializedFunctionPayload> parsedFunctionsByPayload,
        AiFunctionPayloadCoverage coverage)
    {
        uint[] linkedFunctionKeys = model.References.Where(functionNames.ContainsKey)
            .Distinct().ToArray();
        coverage.ModelFunctionReferences += linkedFunctionKeys.Length;
        if (linkedFunctionKeys.Length == 0) return [];

        var linkedFunctionNames = linkedFunctionKeys.ToDictionary(key => key, key => functionNames[key]);
        var mutations = new List<OwnerTrackMutation>();
        foreach (uint functionKey in linkedFunctionKeys)
        {
            if (!resources.TryGetValue(('r', functionKey), out RecordView? functionRecord))
            {
                coverage.MissingFunctionResources++;
                continue;
            }
            coverage.ResolvedFunctionResources++;
            ReadOnlySpan<byte> payload = functionRecord.Source.AsSpan(functionRecord.Offset, functionRecord.Size);
            string payloadHash = Convert.ToHexString(SHA256.HashData(payload));
            if (!parsedFunctionsByPayload.TryGetValue(payloadHash, out SerializedFunctionPayload? parsedPayload))
            {
                if (PcSerializedAiFunctionReader.TryRead(payload, out PcSerializedAiFunction? parsedFunction,
                        out _))
                {
                    parsedPayload = new(parsedFunction, "FullSerializedBody");
                    coverage.FullSerializedBodies++;
                }
                else if (PcCompiledScriptFunctionReader.TryRead(payload,
                             out PcCompiledScriptFunction? compactFunction, out _))
                {
                    parsedPayload = new(null, "CompactMetadataOnly");
                    coverage.CompactMetadataOnly++;
                }
                else
                {
                    parsedPayload = new(null, "UnsupportedPayload");
                    coverage.UnsupportedPayloads++;
                }
                parsedFunctionsByPayload.Add(payloadHash, parsedPayload);
            }
            PcSerializedAiFunction? function = parsedPayload.Function;
            if (function == null) continue;

            foreach (PcAiTrackMutation mutation in PcAiTrackMutationAnalyzer.Analyze(function,
                         linkedFunctionNames))
            {
                mutations.Add(new(functionKey.ToString("X8"), functionNames[functionKey],
                    mutation.NodeIndex, mutation.Operation, mutation.TrackIndex,
                    mutation.TargetKey is { } targetKey ? targetKey.ToString("X8") : null,
                    mutation.TargetName, mutation.Status));
            }
        }

        return mutations.OrderBy(mutation => mutation.FunctionName, StringComparer.Ordinal)
            .ThenBy(mutation => mutation.NodeIndex).ToArray();
    }

    private static SourceFlagAccessOwner? BuildSourceFlagAccessOwner(OwnerVariableSnapshot snapshot,
        IReadOnlyDictionary<uint, HashSet<string>> functionsByModel)
    {
        uint ownerKey = Convert.ToUInt32(snapshot.OwnerKey, 16);
        uint brainKey = Convert.ToUInt32(snapshot.BrainKey, 16);
        uint modelKey = Convert.ToUInt32(snapshot.ModelKey, 16);
        string[] callbackNames = (snapshot.CallbackSlots ?? []).Where(slot => slot.FunctionName != null)
            .Select(slot => slot.FunctionName!).Distinct(StringComparer.Ordinal).ToArray();
        CinematicScriptFlagAccess[] sourceFlagAccesses = [];
        if (snapshot.Variables != null && snapshot.CallbackSlots != null)
        {
            uint worldKey = Convert.ToUInt32(snapshot.WorldKey, 16);
            uint eventsKey = Convert.ToUInt32(snapshot.EventsKey, 16);
            uint sceneEventsKey = Convert.ToUInt32(snapshot.SceneEventsKey, 16);
            var owner = new CinematicScriptOwner(ownerKey, brainKey, modelKey, worldKey, eventsKey,
                sceneEventsKey, snapshot.OwnerName, callbackNames, [], ConvertVariables(snapshot.Variables))
            {
                CallbackSlots = snapshot.CallbackSlots,
                ModelFunctionNames = functionsByModel.GetValueOrDefault(modelKey)?.ToArray() ?? []
            };
            sourceFlagAccesses = CinematicScriptFlagFlowAnalyzer.GetAccesses(owner);
        }
        if (sourceFlagAccesses.Length == 0 && snapshot.EventFlagAccesses.Length == 0) return null;

        return new(snapshot.Container, snapshot.WorldKey, snapshot.WorldName, snapshot.OwnerKey,
            snapshot.OwnerName, snapshot.ModelKey, callbackNames, sourceFlagAccesses,
            snapshot.EventFlagAccesses);
    }

    private static EventFlagAccess[] ReadLinkedEventFlagAccesses(RecordView owner,
        IReadOnlyDictionary<(char Kind, uint Key), RecordView> resources,
        IDictionary<(string Container, uint Key), AnimationClip> decodedClips)
    {
        var accesses = new List<EventFlagAccess>();
        AddLinkedList("Events", owner.EventsKey);
        AddLinkedList("SceneEvents", owner.SceneEventsKey);
        return accesses.DistinctBy(access => (access.LinkName, access.EventKey, access.TrackIndex,
                access.Time, access.TargetKey, access.Flag, access.Function))
            .OrderBy(access => access.LinkName, StringComparer.Ordinal)
            .ThenBy(access => access.EventKey, StringComparer.Ordinal)
            .ThenBy(access => access.TrackIndex)
            .ThenBy(access => access.Time)
            .ToArray();

        void AddLinkedList(string linkName, uint eventKey)
        {
            if (eventKey is <= 2 or uint.MaxValue
                || !resources.TryGetValue(('i', eventKey), out var eventRecord)) return;
            var cacheKey = (eventRecord.Container, eventRecord.Key);
            if (!decodedClips.TryGetValue(cacheKey, out var clip))
            {
                try
                {
                    clip = AnimationReader.Read(eventRecord.Source, eventRecord.Offset, eventRecord.Size);
                    decodedClips.Add(cacheKey, clip);
                }
                catch (Exception exception) when (exception is InvalidDataException or ArgumentException
                    or OverflowException or IndexOutOfRangeException)
                {
                    return;
                }
            }

                for (int trackIndex = 0; trackIndex < clip.Tracks.Count; trackIndex++)
            {
                var channelClip = new AnimationClip
                {
                    Key = eventRecord.Key,
                    Name = eventRecord.Name,
                    CinematicOwnerKey = owner.Key,
                    CinematicOwnerKeys = [owner.Key],
                    CinematicWorldKey = owner.World,
                    Loaded = true,
                    Tracks = [clip.Tracks[trackIndex]],
                    Duration = clip.Duration
                };
                foreach (var (time, call) in CinematicLiteralCall.ReadTimeline(channelClip))
                {
                    string function = CinematicEngineFunctionCatalog.NameFor(call.Selector);
                    bool sets = function == "ARRGlobalIntFlagSet";
                    bool clears = function == "ARRGlobalIntFlagClear";
                    bool tests = function == "ARRGlobalIntFlagTest";
                    if ((!sets && !clears && !tests) || call.Arguments.Length == 0
                        || call.Arguments[0] is 0 or > int.MaxValue) continue;

                    int flag = (int)call.Arguments[0];
                    string access = tests ? "reads" : "writes";
                    string effect = sets ? "sets" : clears ? "clears" : "tests";
                    accesses.Add(new(linkName, eventRecord.Key.ToString("X8"), eventRecord.Name,
                        trackIndex, time, call.Target.ToString("X8"), flag, access, function,
                        sets ? true : clears ? false : (bool?)null,
                        $"{linkName} event track {trackIndex} at {time.ToString("G", CultureInfo.InvariantCulture)}s {effect} this global saved flag"));
                }
            }
        }
    }

    private static string DescribeFlagStateRelation(bool? writtenSet, bool? requiredSet) =>
        (writtenSet, requiredSet) switch
        {
            (true, true) or (false, false) => "configured write matches reader predicate; runtime order is unknown",
            (true, false) or (false, true) => "configured write opposes reader predicate; other writes or branches may apply",
            _ => "configured state compatibility is unknown"
        };

    private static CinematicScriptVariable[] ConvertVariables(CinematicOwnerDefaults.Snapshot snapshot)
    {
        var converted = new List<CinematicScriptVariable>(snapshot.Variables.Length);
        foreach (var variable in snapshot.Variables)
        {
            string source = variable.Source.StartsWith("instance", StringComparison.OrdinalIgnoreCase)
                ? "instance" : "model";
            string value = string.Empty;
            int[]? integerArrayValues = null;
            int? arrayValueCount = null;
            if (variable.Type == PcVariableFormat.IntegerType)
            {
                byte[] rawBytes = Convert.FromHexString(variable.ValueBytes);
                int dimensions = checked((int)(variable.PackedLength >> 30));
                int elementCount = checked((int)(variable.PackedLength & 0x3FFFFFFF));
                int prefixBytes = variable.PackedLength == 1 ? 0 : checked(dimensions * sizeof(uint));
                int requiredBytes = checked(prefixBytes + elementCount * sizeof(int));
                if (requiredBytes <= rawBytes.Length && elementCount > 0)
                {
                    integerArrayValues = Enumerable.Range(0, elementCount)
                        .Select(index => BinaryPrimitives.ReadInt32LittleEndian(
                            rawBytes.AsSpan(prefixBytes + index * sizeof(int), sizeof(int))))
                        .ToArray();
                    if (elementCount == 1 && dimensions == 0)
                        value = integerArrayValues[0].ToString(CultureInfo.InvariantCulture);
                    else
                        arrayValueCount = elementCount;
                }
            }

            converted.Add(new(variable.Name, variable.Type, source, value,
                integerArrayValues, arrayValueCount));
        }

        return converted.ToArray();
    }

    private static object BuildCineEventsSummary(OwnerVariableSnapshot snapshot)
    {
        int? ripleyMode = ReadInteger(snapshot.Variables, "des_i_ripley");
        int? associatedTrue = ReadInteger(snapshot.Variables, "des_i_SPFlag_associe_vrai");
        int? associatedFalse = ReadInteger(snapshot.Variables, "des_i_SPFlag_associe_faux");
        string[] associatedConditions = new[]
        {
            DescribeFlag(associatedTrue, mustBeSet: true),
            DescribeFlag(associatedFalse, mustBeSet: false)
        }.Where(condition => condition != null).Cast<string>().ToArray();
        string replaySuppression = ripleyMode == 0 && associatedConditions.Length > 0
            ? $"Owner is destroyed at initialization if all associated conditions match: {string.Join(" and ", associatedConditions)}."
            : ripleyMode is > 0
                ? "Nonzero des_i_ripley bypasses the associated-flag initialization suppression."
                : "Associated-flag initialization suppression is unknown from these OVA values.";

        return new
        {
            snapshot.Container,
            snapshot.WorldKey,
            snapshot.WorldName,
            snapshot.OwnerKey,
            snapshot.OwnerName,
            snapshot.ModelKey,
            snapshot.EventsKey,
            snapshot.SceneEventsKey,
            DerivedStartGateMask = DeriveCineEventsStartMask(snapshot.Variables),
            StartFlagConditions = new[]
            {
                DescribeFlag(ReadInteger(snapshot.Variables, "des_i_specialFlag_test1"), mustBeSet: true),
                DescribeFlag(ReadInteger(snapshot.Variables, "des_i_specialFlag_test2"), mustBeSet: true)
            }.Where(condition => condition != null),
            AssociatedFlagConditions = associatedConditions,
            RipleyMode = ripleyMode,
            ReplaySuppression = replaySuppression
        };
    }

    private static bool HasConfiguredMessageGate(OwnerVariableSnapshot snapshot,
        IReadOnlyDictionary<uint, HashSet<string>> callbacksByModel)
    {
        if (snapshot.Variables == null || !HasObjectReference(snapshot.Variables, "des_o_acteur_msg"))
            return false;
        if (!uint.TryParse(snapshot.ModelKey, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out uint modelKey))
            return false;

        var callbacks = callbacksByModel.GetValueOrDefault(modelKey);
        return callbacks?.Contains("Cine_Events_cine_tests_depart") == true
            || callbacks?.Contains("helper_text_hlptxt_tests_depart") == true;
    }

    private static object BuildMessageGateSummary(OwnerVariableSnapshot snapshot,
        IReadOnlyDictionary<uint, HashSet<string>> callbacksByModel)
    {
        uint modelKey = Convert.ToUInt32(snapshot.ModelKey, 16);
        var callbacks = callbacksByModel.GetValueOrDefault(modelKey) ?? [];
        return new
        {
            snapshot.Container,
            snapshot.WorldKey,
            snapshot.WorldName,
            snapshot.OwnerKey,
            snapshot.OwnerName,
            snapshot.ModelKey,
            MessageQueue = snapshot.Variables == null ? null
                : ReadObjectKey(snapshot.Variables, "des_o_acteur_msg")?.ToString("X8"),
            ExpectedMessageId = ReadInteger(snapshot.Variables, "des_i_msg_ID"),
            Int1Mask = ReadInteger(snapshot.Variables, "des_i_msg_INT1"),
            CallbackFamily = callbacks.Contains("Cine_Events_cine_tests_depart")
                ? "Cine_Events" : "helper_text_hlptxt",
            snapshot.EventsKey,
            snapshot.SceneEventsKey
        };
    }

    private static int DeriveCineEventsStartMask(CinematicOwnerDefaults.Snapshot? snapshot)
    {
        if (snapshot == null) return 0;
        int mask = 0;
        if (HasObjectReference(snapshot, "des_o_actor_zone1")
            || ReadInteger(snapshot, "des_i_actor_type1") is not 0 and not null) mask |= 0x001;
        if (HasObjectReference(snapshot, "des_o_actor_zone2")) mask |= 0x002;
        if (HasObjectReference(snapshot, "des_o_actor_status1")) mask |= 0x004;
        if (HasObjectReference(snapshot, "des_o_actor_capa1")) mask |= 0x008;
        if (HasObjectReference(snapshot, "des_o_actor_cbit1")) mask |= 0x010;
        if (HasObjectReference(snapshot, "des_o_acteur_msg")) mask |= 0x020;
        if (HasObjectReference(snapshot, "des_o_actor_visible1")) mask |= 0x040;
        if (ReadInteger(snapshot, "des_i_specialFlag_test1") is not 0 and not null) mask |= 0x080;
        if (ReadInteger(snapshot, "des_i_specialFlag_test2") is not 0 and not null) mask |= 0x100;
        if (HasObjectReference(snapshot, "des_o_actor_desstruct")) mask |= 0x200;
        return mask;
    }

    private static bool HasObjectReference(CinematicOwnerDefaults.Snapshot snapshot, string name) =>
        ReadObjectKey(snapshot, name) is > 0 and < uint.MaxValue;

    private static int? ReadInteger(CinematicOwnerDefaults.Snapshot? snapshot, string name)
    {
        var variable = snapshot?.Variables.LastOrDefault(candidate => candidate.Name == name
            && candidate.Type == PcVariableFormat.IntegerType);
        return ReadScalar(variable?.ValueBytes) is uint value ? unchecked((int)value) : null;
    }

    private static uint? ReadObjectKey(CinematicOwnerDefaults.Snapshot snapshot, string name)
    {
        var variable = snapshot.Variables.LastOrDefault(candidate => candidate.Name == name
            && candidate.Type == PcVariableFormat.ObjectReferenceType);
        return ReadScalar(variable?.ValueBytes);
    }

    private static uint? ReadScalar(string? bytesHex)
    {
        if (bytesHex == null) return null;
        byte[] bytes;
        try { bytes = Convert.FromHexString(bytesHex); }
        catch (FormatException) { return null; }
        if (bytes.Length < sizeof(uint)) return null;
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    private static string? DescribeFlag(int? encodedFlag, bool mustBeSet)
    {
        if (encodedFlag is not > 0) return null;
        bool inverted = encodedFlag > 10000;
        int flag = inverted ? encodedFlag.Value - 10000 : encodedFlag.Value;
        bool requiredSet = inverted ? !mustBeSet : mustBeSet;
        return $"{flag} {(requiredSet ? "set" : "clear")}";
    }

    private static void AddNamedResourceCandidates(
        Dictionary<uint, HashSet<string>> namesByKey, IEnumerable<RecordView> records)
    {
        foreach (RecordView record in records.Where(record => record.Name.Length > 0
            && record.Key is not 0 and not uint.MaxValue))
        {
            if (!namesByKey.TryGetValue(record.Key, out var candidates))
                namesByKey.Add(record.Key, candidates = new(StringComparer.OrdinalIgnoreCase));
            candidates.Add($"{record.Container}:{record.World:X8} [{record.Kind}] {record.Name}");
        }
    }

    private static Dictionary<(char Kind, uint Key), RecordView> ResolveOwnerResources(
        IReadOnlyList<RecordView> fixedRecords, IReadOnlyList<RecordView> sceneRecords,
        string preferredContainer, uint ownerWorldKey, out HashSet<(char Kind, uint Key)> ambiguousKeys)
    {
        var candidates = fixedRecords.Concat(sceneRecords)
            .Where(record => record.Container == "FF0084DF" || record.World == 0 || record.World == ownerWorldKey)
            .GroupBy(record => (record.Kind, record.Key));
        var resolved = new Dictionary<(char Kind, uint Key), RecordView>();
        ambiguousKeys = [];
        foreach (var group in candidates)
        {
            RecordView[] preferred = group.Where(record => record.Container == preferredContainer).ToArray();
            RecordView[] inScope = preferred.Length > 0 ? preferred : group.ToArray();
            string[] payloadVariants = inScope.Select(record => Convert.ToHexString(
                    SHA256.HashData(record.Source.AsSpan(record.Offset, record.Size))))
                .Distinct(StringComparer.Ordinal).Take(2).ToArray();
            if (payloadVariants.Length != 1)
            {
                ambiguousKeys.Add(group.Key);
                continue;
            }

            resolved.Add(group.Key, inScope[0]);
        }
        return resolved;
    }

    private static List<RecordView> ReadRecords(byte[] bytes, string archivePath, string container)
    {
        var loader = typeof(SceneActor).Assembly.GetType("JadeView.PcSceneLoader")!;
        var read = loader.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)!;
        object replay = read.Invoke(null, [bytes, archivePath, null])!;
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
        uint worldKey, IReadOnlyDictionary<(uint WorldKey, uint ObjectKey), string> sceneObjectNames) => snapshot.Variables
        .Where(variable => variable.Type == 40)
        .SelectMany(variable => ReadVariableObjectKeys(variable)
            .Where(key => key is not 0 and not uint.MaxValue)
            .Select(key => new OwnerObjectReference(variable.Name, key.ToString("X8"),
                sceneObjectNames.GetValueOrDefault((worldKey, key)), variable.Source)))
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
