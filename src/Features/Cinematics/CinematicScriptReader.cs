using System.Reflection;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO.Compression;

namespace JadeView;

internal static class CinematicScriptReader
{
    private const string FunctionMapResource = "JadeView.CinematicScriptModelFunctions";
    private const string FunctionNamesResource = "JadeView.CinematicScriptFunctionNames";
    private const string FunctionCallsResource = "JadeView.CinematicScriptFunctionCalls";
    private const string CallbackDispatchResource = "JadeView.CinematicScriptCallbackDispatch";
    private const string WorldSchedulerOperationsResource = "JadeView.CinematicWorldSchedulerOperations";
    private const string EngineCallsResource = "JadeView.CinematicScriptEngineCalls";
    private const string MessageReadersResource = "JadeView.CinematicScriptMessageReaders";
    private const string SourceReferencesResource = "JadeView.CinematicScriptSourceReferences";
    private const string OvaFieldAccessesResource = "JadeView.CinematicScriptOvaFieldAccesses";
    private static readonly Lazy<IReadOnlyDictionary<uint, string[]>> FunctionMap = new(ReadFunctionMap);
    private static readonly Lazy<IReadOnlyDictionary<uint, string>> FunctionNames = new(ReadFunctionNames);
    private static readonly Lazy<IReadOnlyDictionary<string, CinematicScriptFunctionCall[]>> FunctionCalls = new(ReadFunctionCalls);
    private static readonly Lazy<CinematicScriptCallbackDispatchSite[]> CallbackDispatchSites = new(ReadCallbackDispatchSites);
    private static readonly Lazy<IReadOnlyDictionary<string, CinematicWorldSchedulerOperation[]>> WorldSchedulerOperations = new(ReadWorldSchedulerOperations);
    private static readonly Lazy<IReadOnlyDictionary<string, CinematicScriptEngineCallSite[]>> EngineCalls = new(ReadEngineCalls);
    private static readonly Lazy<IReadOnlyDictionary<string, CinematicScriptMessageReaderSite[]>> MessageReaders = new(ReadMessageReaders);
    private static readonly Lazy<IReadOnlyDictionary<string, CinematicScriptSourceReferenceSite[]>> SourceReferences = new(ReadSourceReferences);
    private static readonly Lazy<IReadOnlyDictionary<string, CinematicScriptOvaFieldAccessSite[]>> OvaFieldAccesses = new(ReadOvaFieldAccesses);

    internal static int CallbackCatalogModelCount => FunctionMap.Value.Count;
    internal static string[] CallbacksFor(uint modelKey) => FunctionMap.Value.GetValueOrDefault(modelKey) ?? [];
    internal static bool HasCallbackCatalog(uint modelKey) => FunctionMap.Value.ContainsKey(modelKey);
    internal static string DescribeCallbackName(string functionName) =>
        string.Equals(functionName, "sc_dummy", StringComparison.Ordinal)
            ? "sc_dummy (source stub; behavior unknown)"
            : functionName;
    internal static string? FunctionNameFor(uint functionKey) => FunctionNames.Value.GetValueOrDefault(functionKey);
    internal static string[] EffectiveCallbackNames(CinematicScriptCallbackSlot[]? callbackSlots) =>
        callbackSlots?.Where(slot => slot.FunctionName != null)
            .Select(slot => slot.FunctionName!).ToArray() ?? [];

    internal static bool TryDescribeCallbackReachability(CinematicScriptOwner owner, string functionName,
        out string description)
    {
        if (owner.CallbackNames.Contains(functionName, StringComparer.Ordinal))
        {
            description = $"{functionName} is assigned in an owner callback slot";
            return true;
        }

        var path = NestedCallbackPaths(owner).FirstOrDefault(candidate => candidate.FunctionName == functionName);
        description = path?.Path ?? "";
        return path != null;
    }

    internal static CinematicScriptFunctionPath[] NestedCallbackPaths(CinematicScriptOwner owner)
    {
        var modelFunctions = owner.ModelFunctionNames.ToHashSet(StringComparer.Ordinal);
        var roots = owner.CallbackNames.ToHashSet(StringComparer.Ordinal);
        var pending = new Queue<string>();
        var previous = new Dictionary<string, (string Parent, string Dispatch, int? TrackIndex,
            string? IndirectField, string? AssignmentFunction, string? AssignmentSource, int? AssignmentLine,
            string? ReferenceVariable, string? ResolvedFunctionKey)>(StringComparer.Ordinal);
        foreach (string root in roots.OrderBy(functionName => functionName, StringComparer.Ordinal))
        {
            if (!modelFunctions.Contains(root)
                || !previous.TryAdd(root, ("", "", null, null, null, null, null, null, null))) continue;
            pending.Enqueue(root);
        }

        while (pending.TryDequeue(out string? current))
        {
            foreach (var edge in FunctionCalls.Value.GetValueOrDefault(current, [])
                         .OrderBy(edge => edge.To, StringComparer.Ordinal)
                         .ThenBy(edge => edge.Dispatch, StringComparer.Ordinal)
                         .ThenBy(edge => edge.TrackIndex))
            {
                if (!IsReachableDispatch(edge.Dispatch)
                    || !modelFunctions.Contains(edge.To)
                    || !previous.TryAdd(edge.To, (current, edge.Dispatch, edge.TrackIndex,
                        edge.IndirectField, edge.AssignmentFunction, edge.AssignmentSource, edge.AssignmentLine,
                        edge.ReferenceVariable, edge.ResolvedFunctionKey)))
                    continue;
                pending.Enqueue(edge.To);
            }
        }

        return previous.Keys.Where(functionName => !roots.Contains(functionName))
            .Select(functionName => new CinematicScriptFunctionPath(functionName,
                FormatPath(functionName, previous)))
            .OrderBy(path => path.FunctionName, StringComparer.Ordinal)
            .ToArray();

        static string FormatPath(string endpoint,
            IReadOnlyDictionary<string, (string Parent, string Dispatch, int? TrackIndex,
                string? IndirectField, string? AssignmentFunction, string? AssignmentSource, int? AssignmentLine,
                string? ReferenceVariable, string? ResolvedFunctionKey)> parents)
        {
            var path = new List<(string Function, string? Dispatch)>();
            string current = endpoint;
            while (parents.TryGetValue(current, out var step))
            {
                string? dispatchDescription = FormatDispatch(step, current);
                path.Add((current, dispatchDescription));
                if (string.IsNullOrEmpty(step.Parent)) break;
                current = step.Parent;
            }
            path.Reverse();
            if (path.Count == 0) return endpoint;
            string result = path[0].Function;
            for (int pathIndex = 1; pathIndex < path.Count; pathIndex++)
                result += $" -[{path[pathIndex].Dispatch}]-> {path[pathIndex].Function}";
            return result;

            static string? FormatDispatch((string Parent, string Dispatch, int? TrackIndex,
                string? IndirectField, string? AssignmentFunction, string? AssignmentSource, int? AssignmentLine,
                string? ReferenceVariable, string? ResolvedFunctionKey) step,
                string targetFunction)
            {
                if (string.IsNullOrEmpty(step.Parent)) return null;
                string dispatchLabel = step.Dispatch == "track-change" && step.TrackIndex is { } trackIndex
                    ? $"track-change:{trackIndex}" : step.Dispatch;
                if (step.IndirectField is { Length: > 0 })
                {
                    string assignmentSource = step.AssignmentSource is { Length: > 0 } source
                        ? $" at {source}:{step.AssignmentLine}" : "";
                    string assignmentFunction = step.AssignmentFunction is { Length: > 0 } functionName
                        ? $" in {functionName}" : "";
                    dispatchLabel += $" · through pointer field {step.IndirectField}{assignmentFunction}{assignmentSource}";
                }
                else if (step.ReferenceVariable is { Length: > 0 } referenceVariable)
                {
                    string resolvedKey = step.ResolvedFunctionKey is { Length: > 0 } key
                        ? $" resolved as function key {key}"
                        : "";
                    string assignmentSource = step.AssignmentSource is { Length: > 0 } source
                        ? $" at {source}:{step.AssignmentLine}" : "";
                    dispatchLabel += $" · {referenceVariable}{resolvedKey}{assignmentSource}";
                }
                string? api = step.Dispatch switch
                {
                    "execute" => "AIExecute",
                    "set-current-track" => "AITrackCurChange",
                    "replace-current-track" => "AITrackCurChangeNow",
                    "track-change" => "AITrackChange",
                    _ when step.Dispatch.StartsWith("register-callback:", StringComparison.Ordinal) => "AICBAdd",
                    _ when step.Dispatch.StartsWith("unregister-callback:", StringComparison.Ordinal) => "AICBDel",
                    _ => null
                };
                if (api == null) return dispatchLabel;

                var sourceSites = EngineCalls.Value.GetValueOrDefault(step.Parent, [])
                    .Where(call => call.Api == api && call.Arguments.Any(argument =>
                        step.IndirectField is { Length: > 0 }
                            ? Regex.IsMatch(argument,
                                $"(?<![A-Za-z0-9_])(?:[A-Za-z_][A-Za-z0-9_]*\\s*->\\s*){Regex.Escape(step.IndirectField)}(?![A-Za-z0-9_])")
                            : step.ReferenceVariable is { Length: > 0 } referenceVariable
                                ? Regex.IsMatch(argument,
                                    $"(?<![A-Za-z0-9_]){Regex.Escape(referenceVariable)}(?![A-Za-z0-9_])")
                            : Regex.IsMatch(argument,
                                $"(?<![A-Za-z0-9_])fctr?_{Regex.Escape(step.Parent)}_{Regex.Escape(targetFunction)}(?![A-Za-z0-9_])")))
                    .Select(call =>
                    {
                        string sourceBranches = call.BranchConditions.Length == 0
                            ? "no enclosing if recorded"
                            : string.Join(" and ", call.BranchConditions.Select(condition =>
                                condition.Kind == "else"
                                    ? $"else of if ({condition.Expression})"
                                    : $"if ({condition.Expression})"));
                        return $"{Path.GetFileName(call.Source)}:{call.Line} [{sourceBranches}]";
                    })
                    .Distinct(StringComparer.Ordinal)
                    .Take(4).ToArray();
                return sourceSites.Length == 0 ? dispatchLabel
                    : $"{dispatchLabel} · source dispatch sites: {string.Join(" or ", sourceSites)}";
            }
        }

        static bool IsReachableDispatch(string dispatch) => dispatch is "execute"
            or "set-current-track" or "replace-current-track" or "track-change"
            || dispatch.StartsWith("register-callback:", StringComparison.Ordinal);
    }

    internal static IReadOnlyDictionary<string, CinematicScriptAiTrackSlotProvenance>
        PossibleCallbackTrackSlots(CinematicScriptOwner owner) =>
        CinematicScriptAiTrackSlotProvenanceAnalyzer.Analyze(owner.CallbackSlots,
            FunctionCalls.Value, owner.ModelFunctionNames.ToHashSet(StringComparer.Ordinal));

    internal static CinematicScriptCallbackDispatchSite[] DispatchSitesFor(CinematicScriptFunctionPath[] paths)
    {
        var registeredIds = paths.SelectMany(path => Regex.Matches(path.Path, @"register-callback:(-?\d+)")
                .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)))
            .ToHashSet();
        return CallbackDispatchSites.Value.Where(site => registeredIds.Contains(site.CallbackId))
            .OrderBy(site => site.CallbackId).ThenBy(site => site.Source, StringComparer.Ordinal)
            .ThenBy(site => site.Line).ToArray();
    }

    internal static CinematicScriptCallbackDispatchSite[] EngineDispatchSitesFor(int eventId) =>
        CallbackDispatchSites.Value.Where(site => site.CallbackId == eventId
                && (site.Api is "AI_ExecCallback" or "AI_ExecCallbackAll"))
            .OrderBy(site => site.Source, StringComparer.Ordinal)
            .ThenBy(site => site.Line).ToArray();

    internal static CinematicScriptWorldSchedulerOperation[] ReachableWorldSchedulerOperations(CinematicScriptOwner owner)
    {
        var reachablePaths = ReachableCallbackPaths(owner);
        return reachablePaths.SelectMany(path => WorldSchedulerOperations.Value
                .GetValueOrDefault(path.Key, [])
                .Select(operation => new CinematicScriptWorldSchedulerOperation(path.Key,
                    operation.Operation, operation.Arguments, operation.ArgumentSources, operation.Source,
                    operation.Line, path.Value)))
            .OrderBy(operation => operation.FunctionName, StringComparer.Ordinal)
            .ThenBy(operation => operation.Source, StringComparer.Ordinal)
            .ThenBy(operation => operation.Line).ToArray();
    }

    internal static CinematicScriptReachableEngineCall[] ReachableEngineCalls(CinematicScriptOwner owner)
    {
        var reachablePaths = ReachableCallbackPaths(owner);
        return reachablePaths.SelectMany(path => EngineCalls.Value
                .GetValueOrDefault(path.Key, [])
                .Select(call => new CinematicScriptReachableEngineCall(path.Key, call.Api,
                    call.Variant, call.Arguments, call.ArgumentAssignments, call.ArgumentFields,
                    call.MessageFields, call.BranchConditions, call.Source, call.Line, path.Value,
                     call.LoopContexts, call.ArgumentAssignmentAlternatives, call.OvaFieldWrites,
                    call.MessageFieldWrites)))
            .OrderBy(call => call.Api, StringComparer.Ordinal)
            .ThenBy(call => call.Variant, StringComparer.Ordinal)
            .ThenBy(call => call.FunctionName, StringComparer.Ordinal)
            .ThenBy(call => call.Line).ToArray();
    }

    internal static CinematicScriptMessageReader[] ReachableMessageReaders(CinematicScriptOwner owner)
    {
        var reachablePaths = ReachableCallbackPaths(owner);
        var explicitIdReads = ReachableEngineCalls(owner).Where(call => call.Api == "MSGGetByID"
                && call.Arguments.Length >= 2
                && call.Arguments[0] == "AI_gpst_CurrentGameObject"
                && TryParseMessageId(call.Arguments[1], out _))
            .Select(call =>
            {
                _ = TryParseMessageId(call.Arguments[1], out int messageId);
                return new CinematicScriptMessageReader(messageId, call.FunctionName, call.Source,
                    call.Line, call.CallbackPath, "MSGGetByID", "AI_gpst_CurrentGameObject");
            });
        var genericReads = reachablePaths.SelectMany(path => MessageReaders.Value
                .GetValueOrDefault(path.Key, [])
                .Select(reader => new CinematicScriptMessageReader(reader.MessageId, path.Key,
                    reader.Source, reader.Line, path.Value, reader.ReadApi, reader.QueueObject)));
        return explicitIdReads.Concat(genericReads)
            .OrderBy(reader => reader.MessageId)
            .ThenBy(reader => reader.FunctionName, StringComparer.Ordinal)
            .ThenBy(reader => reader.Source, StringComparer.Ordinal)
            .ThenBy(reader => reader.Line)
            .DistinctBy(reader => (reader.MessageId, reader.FunctionName, reader.Source,
                reader.Line, reader.QueueObject)).ToArray();
    }

    internal static CinematicScriptMessageReader[] ReachableMessageReaders(
        CinematicScriptOwner owner, int messageId) => ReachableMessageReaders(owner)
        .Where(reader => reader.MessageId == messageId).ToArray();

    private static bool TryParseMessageId(string expression, out int messageId) =>
        int.TryParse(expression, NumberStyles.Integer, CultureInfo.InvariantCulture, out messageId);

    internal static bool HasSourceGlobalFlagApi(CinematicScriptOwner owner) =>
        owner.CallbackNames.Concat(owner.ModelFunctionNames).Distinct(StringComparer.Ordinal)
            .Any(functionName => EngineCalls.Value.GetValueOrDefault(functionName, []).Any(call =>
                call.Api is "ARRGlobalIntFlagTest" or "ARRGlobalIntFlagSet" or "ARRGlobalIntFlagClear"
                    or "ARRBitTest" or "ARRBitSet" or "ARRBitClear"
                    or "ARRBitSetRange" or "ARRBitClearRange"));

    internal static CinematicScriptEngineCallSummary[] SummarizeReachableEngineCalls(CinematicScriptOwner owner) =>
        ReachableEngineCalls(owner)
            .GroupBy(call => (call.Api, call.Variant))
            .Select(group => new CinematicScriptEngineCallSummary(group.Key.Api, group.Key.Variant,
                group.Count(), group.Take(4).ToArray()))
            .OrderByDescending(summary => summary.CallSiteCount)
            .ThenBy(summary => summary.Api, StringComparer.Ordinal)
            .ThenBy(summary => summary.Variant, StringComparer.Ordinal)
            .ToArray();

    internal static CinematicScriptReachableSourceReference[] ReachableSourceReferences(CinematicScriptOwner owner)
    {
        var reachablePaths = ReachableCallbackPaths(owner);
        return reachablePaths.SelectMany(path => SourceReferences.Value
                .GetValueOrDefault(path.Key, [])
                .Select(reference => new CinematicScriptReachableSourceReference(path.Key, reference.Kind,
                    reference.Symbol, uint.Parse(reference.Key.AsSpan(2), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture), reference.Source,
                    reference.Line, path.Value)))
            .OrderBy(reference => reference.Kind, StringComparer.Ordinal)
            .ThenBy(reference => reference.Symbol, StringComparer.Ordinal)
            .ThenBy(reference => reference.FunctionName, StringComparer.Ordinal)
            .ThenBy(reference => reference.Line).ToArray();
    }

    internal static CinematicScriptReachableOvaFieldAccess[] ReachableOvaFieldAccesses(CinematicScriptOwner owner)
    {
        var reachablePaths = ReachableCallbackPaths(owner);
        return reachablePaths.SelectMany(path => OvaFieldAccesses.Value
                .GetValueOrDefault(path.Key, [])
                .Select(access => new CinematicScriptReachableOvaFieldAccess(access.VariableName,
                    access.Access, access.Source, access.Receiver, access.Expression,
                    access.BranchConditions, access.SourceFile, access.Line, path.Key, path.Value)))
            .OrderBy(access => access.VariableName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(access => access.SourceFile, StringComparer.Ordinal)
            .ThenBy(access => access.Line).ToArray();
    }

    private static Dictionary<string, string> ReachableCallbackPaths(CinematicScriptOwner owner)
    {
        var modelFunctions = owner.ModelFunctionNames.ToHashSet(StringComparer.Ordinal);
        var paths = NestedCallbackPaths(owner).ToDictionary(path => path.FunctionName,
            path => path.Path, StringComparer.Ordinal);
        foreach (string callbackName in owner.CallbackNames.Where(modelFunctions.Contains))
            paths.TryAdd(callbackName, callbackName);
        return paths;
    }

    internal static void Load(IEnumerable<PcSceneLoader.Record> records,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins, SceneDocument document)
    {
        var recordArray = records.ToArray();
        var resources = new PcSceneResourceIndex(recordArray);
        var eventResources = new PcSceneResourceIndex(recordArray.Where(record => record.Kind == 'i'));
        var decodedEventRecords = new Dictionary<PcSceneLoader.Record, AnimationClip>();
        foreach (var owner in recordArray.Where(record => record.Kind == 'l'))
        {
            var embeddedLists = new List<CinematicEmbeddedEventTrackList>();
            // TRA* operates on the GAO's extended->eve data, loaded from SceneEvents.
            // The base Events reference belongs to the actor animation/action path.
            AddEmbeddedTrackList("SceneEvents", owner.SceneEvents);
            if (embeddedLists.Count > 0)
                document.CinematicEmbeddedEventTracks[(owner.LoadWorldKey, owner.Key)] = embeddedLists.ToArray();

            void AddEmbeddedTrackList(string linkName, uint eventKey)
            {
                if (eventKey is <= 2 or uint.MaxValue) return;
                PcSceneResourceResolution resolution = eventResources.ResolveDetailed(eventKey, owner.LoadWorldKey);
                var eventRecord = resolution.Record;
                if (eventRecord == null)
                {
                    if (resolution.Failure?.StartsWith("multiple ", StringComparison.Ordinal) == true
                        && owner.BrainFieldOffset >= 0)
                        document.Diagnostics.Add($"Embedded event list {eventKey:X8} linked by {owner.Name} is ambiguous: {resolution.Failure}.");
                    return;
                }
                if (!decodedEventRecords.TryGetValue(eventRecord, out var clip))
                {
                    try
                    {
                        clip = AnimationReader.Read(origins[eventRecord], eventRecord.Offset, eventRecord.Size);
                        decodedEventRecords.Add(eventRecord, clip);
                    }
                    catch (Exception exception) when (exception is InvalidDataException or ArgumentException
                        or OverflowException or IndexOutOfRangeException)
                    {
                        if (owner.BrainFieldOffset >= 0)
                            document.Diagnostics.Add($"Embedded event list {eventKey:X8} linked by {owner.Name} could not be decoded: {exception.Message}");
                        return;
                    }
                }

                var tracks = clip.Tracks.Select((track, index) =>
                {
                    var channelClip = new AnimationClip
                    {
                        Key = eventRecord.Key,
                        Name = eventRecord.Name,
                        CinematicOwnerKey = owner.Key,
                        UsesEventTrackExecutionContext = true,
                        Loaded = true,
                        Tracks = [track]
                    };
                    return new CinematicEmbeddedEventTrack(
                        index,
                        track.Target,
                        track.Bone,
                        track.ChannelFlags,
                        track.Keys.Count,
                        track.Keys.Count == 0 ? 0 : track.Keys.Max(key => key.Time + key.Duration))
                    {
                        SourceTrack = track,
                        Calls = CinematicLiteralCall.ReadTimeline(channelClip),
                        Messages = CinematicTimelineMessage.ReadTimeline(channelClip)
                    };
                }).ToArray();
                embeddedLists.Add(new(owner.Key, owner.Name, owner.LoadWorldKey, linkName,
                    eventRecord.Key, tracks));
            }
        }

        foreach (var owner in recordArray.Where(record => record.Kind == 'l' && record.Events is > 2 and < uint.MaxValue))
        {
            PcSceneResourceResolution resolution = eventResources.ResolveDetailed(owner.Events, owner.LoadWorldKey);
            var eventRecord = resolution.Record;
            if (eventRecord == null)
            {
                if (resolution.Failure?.StartsWith("multiple ", StringComparison.Ordinal) == true)
                    document.Diagnostics.Add($"Events {owner.Events:X8} linked by {owner.Name} is ambiguous: {resolution.Failure}.");
                continue;
            }

            try
            {
                if (!decodedEventRecords.TryGetValue(eventRecord, out var sourceClip))
                {
                    sourceClip = AnimationReader.Read(origins[eventRecord], eventRecord.Offset, eventRecord.Size);
                    decodedEventRecords.Add(eventRecord, sourceClip);
                }

                var ownerContextClip = new AnimationClip
                {
                    Key = eventRecord.Key,
                    Name = eventRecord.Name,
                    CinematicOwnerKey = owner.Key,
                    CinematicOwnerKeys = [owner.Key],
                    CinematicWorldKey = owner.LoadWorldKey,
                    Loaded = true,
                    Tracks = sourceClip.Tracks,
                    Duration = sourceClip.Duration
                };
                var messageTrack = new CinematicScriptMessageTrack(owner.LoadWorldKey, owner.Key,
                    "Events", eventRecord.Key, owner.Name,
                    CinematicTimelineMessage.ReadTimeline(ownerContextClip));
                if (messageTrack.Messages.Length > 0)
                    document.CinematicMessageTracks.Add(messageTrack);
            }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentException
                or OverflowException or IndexOutOfRangeException)
            {
                // The optional message index must not prevent unrelated GAOs from loading.
            }
        }

        foreach (var owner in recordArray.Where(record => record.Kind == 'l' && record.BrainFieldOffset >= 0))
        {
            if (!PcScriptModelResolver.TryRead(owner, resources, origins, out var model, out uint instanceKey))
                continue;

            uint brainKey = Preview.U32(origins[owner], owner.BrainFieldOffset);
            var modelFunctionNames = CallbacksFor(model.Key);
            ReadOnlyMemory<byte>? modelVariablePayload =
                PcScriptModelVariableResourceResolver.ResolveModel(model, resources, origins);
            var callbackSlots = modelVariablePayload is { } callbackPayload
                ? PcScriptCallbackSlotReader.Read(callbackPayload.Span, FunctionNames.Value)
                : null;
            var engineCallbackSlots = modelVariablePayload is { } engineCallbackPayload
                ? PcScriptEngineCallbackReader.Read(engineCallbackPayload.Span, FunctionNames.Value)
                : null;
            var functionResources = PcScriptFunctionResourceReader.Read(model, resources, origins);
            var callbackNames = EffectiveCallbackNames(callbackSlots);
            var objectReferences = ReadObjectReferences(model, instanceKey, owner.LoadWorldKey, resources, origins);
            var variables = ReadVariables(model, instanceKey, owner.LoadWorldKey, resources, origins);
            var dialogueLines = ReadDialogueLines(model, instanceKey, owner.LoadWorldKey, resources, origins);
            var ownerId = new CinematicScriptOwnerId(owner.LoadWorldKey, owner.Key);
            document.CinematicScriptOwners[ownerId] = new CinematicScriptOwner(
                owner.Key,
                brainKey,
                model.Key,
                owner.LoadWorldKey,
                owner.Events,
                owner.SceneEvents,
                owner.Name,
                callbackNames,
                objectReferences,
                variables,
                dialogueLines)
            {
                SerializedGaoFlags = owner.Flags,
                SerializedStateFlags = owner.SerializedStateFlags,
                CallbackSlots = callbackSlots,
                EngineCallbackSlots = engineCallbackSlots,
                ModelFunctionNames = modelFunctionNames,
                FunctionResources = functionResources
            };
            var linkedEventTracks = new List<CinematicScriptEventTrack>();
            AddEventTrack("Events", owner.Events);
            AddEventTrack("SceneEvents", owner.SceneEvents);
            if (linkedEventTracks.Count > 0)
                document.CinematicScriptEventTracks[ownerId] = linkedEventTracks.ToArray();

            void AddEventTrack(string linkName, uint eventKey)
            {
                if (eventKey is <= 2 or uint.MaxValue) return;
                PcSceneResourceResolution resolution = eventResources.ResolveDetailed(eventKey, owner.LoadWorldKey);
                var eventRecord = resolution.Record;
                if (eventRecord == null)
                {
                    document.Diagnostics.Add($"Script owner {owner.Name} links {linkName} {eventKey:X8}, but {resolution.Failure}.");
                    return;
                }
                try
                {
                    var clip = AnimationReader.Read(origins[eventRecord], eventRecord.Offset, eventRecord.Size);
                    clip.Key = eventRecord.Key;
                    clip.Name = string.IsNullOrWhiteSpace(eventRecord.Name)
                        ? $"{linkName} · {eventRecord.Key:X8}" : eventRecord.Name;
                    clip.CinematicOwnerKey = owner.Key;
                    clip.CinematicOwnerKeys = [owner.Key];
                    linkedEventTracks.Add(new(owner.LoadWorldKey, owner.Key, linkName, eventRecord.Key, clip.Name,
                        clip.Duration, CinematicLiteralCall.ReadTimeline(clip),
                        CinematicTimelineMessage.ReadTimeline(clip)));
                }
                catch (Exception exception) when (exception is InvalidDataException or ArgumentException
                    or OverflowException or IndexOutOfRangeException)
                {
                    document.Diagnostics.Add($"Script event {eventKey:X8} linked by {owner.Name} could not be decoded: {exception.Message}");
                }
            }
        }
    }

    private static CinematicScriptCallbackSlot[]? ReadCallbackSlots(PcSceneLoader.Record model,
        PcSceneResourceIndex resources,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins)
    {
        return PcScriptCallbackSlotResolver.Resolve(model, resources, origins, FunctionNames.Value);
    }

    private static CinematicDialogueLine[] ReadDialogueLines(PcSceneLoader.Record model, uint instanceKey,
        uint ownerWorldKey, PcSceneResourceIndex resources,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins)
    {
        ReadOnlyMemory<byte> instanceBytes = PcScriptModelVariableResourceResolver.ResolveInstance(
            instanceKey, ownerWorldKey, resources, origins) ?? ReadOnlyMemory<byte>.Empty;
        try
        {
            ReadOnlyMemory<byte>? modelBytes = PcScriptModelVariableResourceResolver.ResolveModel(model, resources, origins);
            return PcDialogueLineTableReader.ReadOverlayBlocks(
                modelBytes is { } bytes ? [bytes] : [], instanceBytes) ?? [];
        }
        catch (InvalidDataException)
        {
            return [];
        }
    }

    private static CinematicScriptVariable[] ReadVariables(PcSceneLoader.Record model, uint instanceKey,
        uint ownerWorldKey, PcSceneResourceIndex resources,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins)
    {
        var variables = new List<CinematicScriptVariable>();
        Append(PcScriptModelVariableResourceResolver.ResolveModel(model, resources, origins), "model");
        Append(PcScriptModelVariableResourceResolver.ResolveInstance(
            instanceKey, ownerWorldKey, resources, origins), "instance");
        return variables.ToArray();

        void Append(ReadOnlyMemory<byte>? payload, string source)
        {
            if (payload is not { } bytes) return;
            try
            {
                variables.AddRange(PcVariableSnapshotReader.Read(bytes.Span, source));
            }
            catch (InvalidDataException)
            {
                // Preserve the script owner if a variable block uses an unknown OVA layout.
            }
        }
    }

    private static CinematicScriptObjectReference[] ReadObjectReferences(PcSceneLoader.Record model, uint instanceKey,
        uint ownerWorldKey, PcSceneResourceIndex resources,
        IReadOnlyDictionary<PcSceneLoader.Record, byte[]> origins)
    {
        try
        {
            ReadOnlyMemory<byte>? modelBytes = PcScriptModelVariableResourceResolver.ResolveModel(model, resources, origins);
            ReadOnlyMemory<byte> instanceBytes = PcScriptModelVariableResourceResolver.ResolveInstance(
                instanceKey, ownerWorldKey, resources, origins) ?? ReadOnlyMemory<byte>.Empty;
            return PcObjectReferenceListReader.ReadOverlayBlocks(modelBytes is { } bytes ? [bytes] : [], instanceBytes.Span)
                .Where(reference => reference.TargetKey is > 0 and < uint.MaxValue
                    && (reference.TargetKey <= 2 || resources.Resolve(reference.TargetKey, ownerWorldKey) != null))
                .Select(reference => new CinematicScriptObjectReference(reference.VariableName,
                    reference.TargetKey, reference.Source,
                    resources.Resolve(reference.TargetKey, ownerWorldKey) is { Kind: 'l' } target
                        ? target.Name : null,
                    reference.ArrayIndex))
                .ToArray();
        }
        catch (InvalidDataException)
        {
            return [];
        }
    }

    private static IReadOnlyDictionary<uint, string[]> ReadFunctionMap()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(FunctionMapResource)
            ?? throw new InvalidDataException($"Embedded script function map '{FunctionMapResource}' is missing.");
        var serialized = JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream)
            ?? throw new InvalidDataException("Embedded script function map is empty.");
        return serialized.ToDictionary(pair => Convert.ToUInt32(pair.Key, 16), pair => pair.Value);
    }

    private static IReadOnlyDictionary<uint, string> ReadFunctionNames()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(FunctionNamesResource)
            ?? throw new InvalidDataException($"Embedded script function names '{FunctionNamesResource}' are missing.");
        var serialized = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidDataException("Embedded script function name map is empty.");
        return serialized.ToDictionary(pair => Convert.ToUInt32(pair.Key, 16), pair => pair.Value);
    }

    private static IReadOnlyDictionary<string, CinematicScriptFunctionCall[]> ReadFunctionCalls()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(FunctionCallsResource)
            ?? throw new InvalidDataException($"Embedded script function calls '{FunctionCallsResource}' is missing.");
        var serialized = JsonSerializer.Deserialize<Dictionary<string, CinematicScriptFunctionCall[]>>(stream)
            ?? throw new InvalidDataException("Embedded script function call graph is empty.");
        return serialized;
    }

    private static CinematicScriptCallbackDispatchSite[] ReadCallbackDispatchSites()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(CallbackDispatchResource)
            ?? throw new InvalidDataException($"Embedded callback dispatch catalog '{CallbackDispatchResource}' is missing.");
        return JsonSerializer.Deserialize<CinematicScriptCallbackDispatchSite[]>(stream) ?? [];
    }

    private static IReadOnlyDictionary<string, CinematicWorldSchedulerOperation[]> ReadWorldSchedulerOperations()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(WorldSchedulerOperationsResource)
            ?? throw new InvalidDataException($"Embedded world scheduler operations '{WorldSchedulerOperationsResource}' are missing.");
        return JsonSerializer.Deserialize<Dictionary<string, CinematicWorldSchedulerOperation[]>>(stream)
            ?? throw new InvalidDataException("Embedded world scheduler operation index is empty.");
    }

    private static IReadOnlyDictionary<string, CinematicScriptEngineCallSite[]> ReadEngineCalls()
    {
        using Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(EngineCallsResource)
            ?? throw new InvalidDataException($"Embedded script engine-call index '{EngineCallsResource}' is missing.");
        using var decompressed = new GZipStream(resource, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<Dictionary<string, CinematicScriptEngineCallSite[]>>(decompressed)
            ?? throw new InvalidDataException("Embedded script engine-call index is empty.");
    }

    private static IReadOnlyDictionary<string, CinematicScriptMessageReaderSite[]> ReadMessageReaders()
    {
        using Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(MessageReadersResource)
            ?? throw new InvalidDataException($"Embedded script message-reader index '{MessageReadersResource}' is missing.");
        using var decompressed = new GZipStream(resource, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<Dictionary<string, CinematicScriptMessageReaderSite[]>>(decompressed)
            ?? throw new InvalidDataException("Embedded script message-reader index is empty.");
    }

    private static IReadOnlyDictionary<string, CinematicScriptSourceReferenceSite[]> ReadSourceReferences()
    {
        using Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(SourceReferencesResource)
            ?? throw new InvalidDataException($"Embedded script source-reference index '{SourceReferencesResource}' is missing.");
        using var decompressed = new GZipStream(resource, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<Dictionary<string, CinematicScriptSourceReferenceSite[]>>(decompressed)
            ?? throw new InvalidDataException("Embedded script source-reference index is empty.");
    }

    private static IReadOnlyDictionary<string, CinematicScriptOvaFieldAccessSite[]> ReadOvaFieldAccesses()
    {
        using Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(OvaFieldAccessesResource)
            ?? throw new InvalidDataException($"Embedded OVA field-access index '{OvaFieldAccessesResource}' is missing.");
        using var decompressed = new GZipStream(resource, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<Dictionary<string, CinematicScriptOvaFieldAccessSite[]>>(decompressed)
            ?? throw new InvalidDataException("Embedded OVA field-access index is empty.");
    }
}
