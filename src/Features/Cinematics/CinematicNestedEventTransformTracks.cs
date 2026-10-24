namespace JadeView;

internal static class CinematicNestedEventTransformTracks
{
    private const int MaximumDepth = 8;
    private const int MaximumScheduledTracks = 512;

    internal static AnimationTrack[] Read(AnimationClip sourceClip, WorldData world, SceneDocument document) =>
        ReadTimeline(sourceClip, world, document).TransformTracks;

    internal static CinematicNestedEventTimeline ReadTimeline(AnimationClip sourceClip, WorldData world,
        SceneDocument document, IReadOnlySet<CinematicScriptOwnerId>? disabledCallbackOwners = null,
        IReadOnlySet<CinematicScriptOwnerId>? previewedSceneEventOwners = null,
        IReadOnlyDictionary<CinematicScriptOwnerId, float>? previewedSceneEventStartTimes = null)
    {
        var sceneLists = document.CinematicEmbeddedEventTracks.Values
            .SelectMany(lists => lists)
            .Where(list => list.LinkName.Equals("SceneEvents", StringComparison.OrdinalIgnoreCase))
            .GroupBy(list => list.OwnerKey)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var commands = CinematicLiteralCall.ReadTimeline(sourceClip);
        var callbackStartsByOwner = document.CinematicScriptOwners.Values
            .Where(owner => owner.WorldKey == world.Key && owner.SceneEventsKey == sourceClip.Key)
            .Select(owner => (Owner: owner, Starts: CinematicScriptEventTrackCallResolver.Resolve(owner,
                    CinematicScriptReader.ReachableEngineCalls(owner), document.CinematicEmbeddedEventTracks)
                .Where(invocation => invocation.EventList.WorldKey == world.Key
                    && invocation.EventList.EventKey == sourceClip.Key
                    && invocation.Operation is "TRAPlay" or "TRAPlayAll")
                .ToArray()))
            .Where(item => item.Starts.Length > 0)
            .ToArray();
        var callbackOwners = callbackStartsByOwner.Select(item => item.Owner.Id).ToArray();
        var callbackOwnerIds = callbackOwners.ToHashSet();
        var previewableOwners = FindPreviewableSceneEventOwners(sourceClip, world, document, sceneLists)
            .Where(item => !callbackOwnerIds.Contains(item.Owner.Id)).ToArray();
        var previewableOwnerIds = previewableOwners.Select(item => item.Owner.Id).ToHashSet();
        var transforms = new List<AnimationTrack>();
        var eventCommands = new List<(float Time, CinematicLiteralCall Call)>();
        var timelineMessages = new List<(float Time, CinematicTimelineMessage Message)>();
        var scheduledMessages = new List<CinematicScheduledMessage>();
        var rotationCommands = new List<CinematicLocalRotationCommand>();
        var moveSegments = new List<GameScriptMoveSegment>();
        var moveDiagnostics = new List<string>();
        var routeDiagnostics = new List<CinematicEventRouteDiagnostic>();
        var executionLedger = new List<CinematicOwnerExecutionLedgerEntry>();
        var channelExecutions = new List<CinematicOwnerChannelExecution>();
        var activeChannels = new HashSet<CinematicNestedEventTrackIdentity>();
        int scheduledTrackCount = 0;

        foreach (var list in sceneLists.Values.SelectMany(lists => lists)
                     .Where(list => list.WorldKey == world.Key
                         && world.Objects.Any(sceneObject => sceneObject.GaoKey == list.OwnerKey)))
        foreach (var channel in list.Tracks.Where(track =>
                     (track.ChannelFlags & CinematicEventTrackFlags.RunningInit) != 0))
        {
            var ownerId = new CinematicScriptOwnerId(list.WorldKey, list.OwnerKey);
            bool ownerHasExplicitPreviewControl = previewableOwnerIds.Contains(ownerId);
            bool callbackOwnerWasDisabled = callbackOwnerIds.Contains(ownerId)
                && disabledCallbackOwners?.Contains(ownerId) == true;
            var previewStatus = callbackOwnerWasDisabled
                ? CinematicOwnerPreviewStatus.Disabled
                : ownerHasExplicitPreviewControl
                    ? CinematicOwnerPreviewStatus.Available
                    : CinematicOwnerPreviewStatus.Included;
            executionLedger.Add(new(ownerId, list.EventKey, CinematicOwnerExecutionRoute.RunningInit,
                previewStatus, CinematicRuntimeGateStatus.Unknown, [channel.Index],
                previewStatus == CinematicOwnerPreviewStatus.Included ? 0 : null,
                "SceneEvents channel has the serialized RunningInit flag.",
                "Preview scheduling does not evaluate live runtime activation, callback timing, or game state."));
            if (ownerHasExplicitPreviewControl || callbackOwnerWasDisabled) continue;

            Schedule(list, channel, 0, 0,
                new HashSet<CinematicNestedEventTrackIdentity>(), commands,
                CinematicOwnerExecutionRoute.RunningInit);
        }

        foreach (var (owner, starts) in callbackStartsByOwner)
        {
            bool callbackDisabled = disabledCallbackOwners?.Contains(owner.Id) == true;
            var callbackChannels = starts.SelectMany(invocation => invocation.Tracks)
                .DistinctBy(channel => channel.Index).Select(channel => channel.Index).Order().ToArray();
            executionLedger.Add(new(owner.Id, sourceClip.Key,
                CinematicOwnerExecutionRoute.CallbackReachability,
                callbackDisabled ? CinematicOwnerPreviewStatus.Disabled : CinematicOwnerPreviewStatus.Included,
                CinematicRuntimeGateStatus.Unknown, callbackChannels,
                callbackDisabled ? null : 0,
                "A source-reachable callback resolves a TRAPlay or TRAPlayAll operation to this SceneEvents list.",
                "Call reachability is static evidence; callback dispatch, start gates, and live timing are not executed.")
            {
                CallbackSources = starts.Select(invocation => BindCallbackSource(owner,
                        invocation.SourceCall.CallbackPath))
                    .Distinct().ToArray()
            });
            if (callbackDisabled) continue;

            foreach (var invocation in starts)
            foreach (var channel in invocation.Tracks)
                Schedule(invocation.EventList, channel, 0, 0,
                    new HashSet<CinematicNestedEventTrackIdentity>(), commands,
                    CinematicOwnerExecutionRoute.CallbackReachability);
        }

        foreach (var (owner, list) in previewableOwners.Where(item =>
                     previewedSceneEventOwners?.Contains(item.Owner.Id) == true))
        {
            float previewStartTime = previewedSceneEventStartTimes?.GetValueOrDefault(owner.Id) ?? 0;
            executionLedger.Add(new(owner.Id, list.EventKey, CinematicOwnerExecutionRoute.ManualPreview,
                CinematicOwnerPreviewStatus.Included, CinematicRuntimeGateStatus.Unknown,
                list.Tracks.Select(channel => channel.Index).Order().ToArray(), previewStartTime,
                "The user explicitly enabled this owner's SceneEvents preview.",
                "Manual preview is an override and does not establish that the game would pass this owner's runtime gates."));
        }
        foreach (var (owner, list) in previewableOwners.Where(item =>
                     previewedSceneEventOwners?.Contains(item.Owner.Id) != true))
            executionLedger.Add(new(owner.Id, list.EventKey, CinematicOwnerExecutionRoute.ManualPreview,
                CinematicOwnerPreviewStatus.Available, CinematicRuntimeGateStatus.Unknown,
                list.Tracks.Select(channel => channel.Index).Order().ToArray(), null,
                "A separate owner SceneEvents list is linked to an active world.",
                "No serialized start route was selected for preview; runtime gate result remains unknown."));

        foreach (var (owner, list) in previewableOwners.Where(item =>
                     previewedSceneEventOwners?.Contains(item.Owner.Id) == true))
        foreach (var channel in list.Tracks)
            Schedule(list, channel,
                previewedSceneEventStartTimes?.GetValueOrDefault(owner.Id) ?? 0,
                0,
                new HashSet<CinematicNestedEventTrackIdentity>(), commands,
                CinematicOwnerExecutionRoute.ManualPreview);

        foreach (var (startTime, call) in commands)
        {
            if (!TrySelectChannels(call, world.Key, sceneLists, out var list, out var tracks,
                    out var routeFailure))
            {
                AddRouteDiagnostic(startTime, sourceClip.Key, sourceClip.Key, null, call, routeFailure);
                continue;
            }

            foreach (var channel in tracks)
                Schedule(list, channel, startTime, 0,
                    new HashSet<CinematicNestedEventTrackIdentity>(), commands,
                    CinematicOwnerExecutionRoute.CinematicEventCall);
        }

        rotationCommands.AddRange(CinematicLocalRotationTimeline.ReadSourceCommands(sourceClip));
        return new(transforms.ToArray(), callbackOwners.Distinct().ToArray(),
            previewableOwners.Select(item => item.Owner.Id).Distinct().ToArray(), eventCommands.DistinctBy(item =>
            (item.Time, item.Call.Target, item.Call.Selector, Arguments: string.Join(",", item.Call.Arguments)))
            .OrderBy(item => item.Time).ToArray(), rotationCommands.ToArray())
        {
            Messages = timelineMessages.DistinctBy(item => item).OrderBy(item => item.Time).ToArray(),
            OwnerExecutionLedger = executionLedger
                .DistinctBy(entry => (entry.OwnerId, entry.EventKey, entry.Route, entry.PreviewStatus,
                    entry.PreviewStartTime, Channels: string.Join(",", entry.ChannelIndices)))
                .OrderBy(entry => entry.OwnerId.WorldKey).ThenBy(entry => entry.OwnerId.OwnerKey)
                .ThenBy(entry => entry.Route).ToArray(),
            OwnerChannelExecutions = channelExecutions.ToArray(),
            ScheduledMessages = scheduledMessages
                .DistinctBy(item => (item.WorldKey, item.EventListOwnerKey, item.EventKey,
                    item.ChannelIndex, item.PreviewRoute, item.TimelineTime, item.Message))
                .OrderBy(item => item.TimelineTime)
                .ThenBy(item => item.WorldKey)
                .ThenBy(item => item.EventListOwnerKey)
                .ThenBy(item => item.ChannelIndex)
                .ToArray(),
            MoveSegments = moveSegments.ToArray(),
            MoveDiagnostics = moveDiagnostics.Distinct(StringComparer.Ordinal).ToArray(),
            RouteDiagnostics = routeDiagnostics.Distinct().OrderBy(item => item.Time)
                .ThenBy(item => item.SourceOwnerKey).ThenBy(item => item.SourceChannelIndex)
                .ThenBy(item => item.TargetOwnerKey).ToArray()
        };

        void AddRouteDiagnostic(float timelineTime, uint sourceOwnerKey, uint sourceEventKey,
            int? sourceChannelIndex, CinematicLiteralCall call,
            CinematicEventRouteFailure failure)
        {
            if (failure == CinematicEventRouteFailure.None) return;
            routeDiagnostics.Add(new(timelineTime, sourceOwnerKey, sourceEventKey,
                sourceChannelIndex, call.Target,
                CinematicEngineFunctionCatalog.NameFor(call.Selector), failure));
        }

        void Schedule(CinematicEmbeddedEventTrackList list, CinematicEmbeddedEventTrack channel,
            float startTime, int depth, HashSet<CinematicNestedEventTrackIdentity> ancestry,
            IReadOnlyList<(float Time, CinematicLiteralCall Call)> lifecycleCommands,
            CinematicOwnerExecutionRoute executionRoute)
        {
            bool invalidScheduleRange = !float.IsFinite(startTime) || startTime < 0
                || startTime > sourceClip.Duration || !float.IsFinite(channel.Duration) || channel.Duration < 0;
            if (depth >= MaximumDepth || scheduledTrackCount >= MaximumScheduledTracks || invalidScheduleRange) return;
            var identity = new CinematicNestedEventTrackIdentity(list.WorldKey, list.OwnerKey,
                list.EventKey, channel.Index);
            if (ancestry.Contains(identity) || !activeChannels.Add(identity)) return;
            scheduledTrackCount++;
            if (executionRoute is CinematicOwnerExecutionRoute.CinematicEventCall
                or CinematicOwnerExecutionRoute.NestedEventCall)
                executionLedger.Add(new(new(list.WorldKey, list.OwnerKey), list.EventKey, executionRoute,
                    CinematicOwnerPreviewStatus.Included, CinematicRuntimeGateStatus.Unknown,
                    [channel.Index], startTime,
                    executionRoute == CinematicOwnerExecutionRoute.CinematicEventCall
                        ? "A literal cinematic TRAPlay/TRAPlayAll command resolves to this channel."
                        : "A nested EVE TRAPlay/TRAPlayAll command resolves to this channel.",
                    "The resolved command is previewed on the shared cinematic clock; live queue and runtime gate state are not simulated."));

            var parentPlaybackClock = CreatePlaybackClock(lifecycleCommands, list, channel,
                startTime, sourceClip.Duration);
            var absoluteChannelCommands = channel.Calls
                .SelectMany(item => MapChannelCommandTimes(item.Time, startTime, parentPlaybackClock)
                    .Select(time => (Time: time, item.Call))).ToArray();
            var channelLifecycleCommands = lifecycleCommands.Concat(absoluteChannelCommands).ToArray();
            CinematicTrackPlaybackClock? playbackClock = CreatePlaybackClock(channelLifecycleCommands, list, channel,
                startTime, sourceClip.Duration);
            var stateClock = playbackClock ?? CreatePlaybackClock(channelLifecycleCommands, list, channel,
                startTime, sourceClip.Duration, createWithoutInterruption: true)!;
            channelExecutions.Add(new(new(list.WorldKey, list.OwnerKey), list.EventKey,
                channel.Index, executionRoute, stateClock));
            AddLocalRotationCommands(channel, startTime, playbackClock);
            moveSegments.AddRange(CinematicGameScriptEventAdapter.ReadMoveSegments(
                list, channel, startTime, sourceClip.Duration, playbackClock, moveDiagnostics));

            eventCommands.AddRange(absoluteChannelCommands);
            foreach (var item in channel.Messages)
            foreach (float timelineTime in MapChannelCommandTimes(item.Time, startTime, playbackClock))
            {
                var scheduledMessage = item.Message with { Time = timelineTime };
                timelineMessages.Add((timelineTime, scheduledMessage));
                CinematicTrackPlaybackSnapshot? channelState = stateClock.TryGetSnapshot(
                    timelineTime, out var snapshot) ? snapshot : null;
                uint? irisQueueOwnerCandidate = scheduledMessage.ExecutionObjectKey is > 2 and < uint.MaxValue
                    ? scheduledMessage.ExecutionObjectKey : null;
                var queueOwnerEvidence = irisQueueOwnerCandidate.HasValue
                    ? CinematicMessageQueueOwnerEvidence.IrisEveTrackContext
                    : CinematicMessageQueueOwnerEvidence.Unresolved;
                scheduledMessages.Add(new(list.WorldKey, list.OwnerKey, list.EventKey,
                    channel.Index, executionRoute, item.Time, timelineTime, scheduledMessage,
                    channelState, irisQueueOwnerCandidate, queueOwnerEvidence,
                    "The recognized 19-row event payload ends in MSGSendAll. Iris EVE dispatch sets CurrentUltra from the target GAO or, when absent, the event-list owner; this matches the decoded track context.",
                    "The key is an Iris-model queue-owner candidate. Whether retail/game runtime executes it, has queue capacity, or dispatches/consumes it is unknown."));
            }

            uint targetKey = channel.TargetKey == 0 ? list.OwnerKey : channel.TargetKey;
            bool targetLoaded = targetKey is > 0 and < uint.MaxValue
                && world.Objects.Any(sceneObject => sceneObject.GaoKey == targetKey);
            if (targetLoaded && CinematicEventTrackPreview.HasTransformData(channel.SourceTrack))
                transforms.Add(ShiftToCinematicTime(channel.SourceTrack!, targetKey, startTime,
                    playbackClock, UsesFlashReference(list, channel, targetKey)));

            var nextAncestry = new HashSet<CinematicNestedEventTrackIdentity>(ancestry) { identity };
            foreach (var (nestedStartTime, nestedCall) in absoluteChannelCommands)
            {
                if (!TrySelectChannels(nestedCall, list.WorldKey, sceneLists, out var nestedList,
                        out var nestedChannels, out var routeFailure))
                {
                    AddRouteDiagnostic(nestedStartTime, list.OwnerKey, list.EventKey,
                        channel.Index, nestedCall, routeFailure);
                    continue;
                }
                foreach (var nestedChannel in nestedChannels)
                    Schedule(nestedList, nestedChannel, nestedStartTime, depth + 1,
                        nextAncestry, channelLifecycleCommands,
                        CinematicOwnerExecutionRoute.NestedEventCall);
            }

            void AddLocalRotationCommands(CinematicEmbeddedEventTrack sourceChannel,
                float channelStartTime, CinematicTrackPlaybackClock? channelClock)
            {
                foreach (var item in sourceChannel.Calls.Where(item =>
                             CinematicEngineFunctionCatalog.NameFor(item.Call.Selector) == "OBJRotateLocalY"
                             && item.Call.Arguments.Length >= 1))
                {
                    float duration = sourceChannel.SourceTrack?.Keys
                        .FirstOrDefault(key => key.Time == item.Time)?.Duration ?? 0;
                    float validDuration = float.IsFinite(duration) ? Math.Max(0, duration) : 0;
                    float radians = BitConverter.UInt32BitsToSingle(item.Call.Arguments[0]);
                    if (!float.IsFinite(radians)) continue;

                    if (channelClock == null)
                    {
                        float timelineStart = channelStartTime + item.Time;
                        float timelineEnd = Math.Min(sourceClip.Duration, timelineStart + validDuration);
                        rotationCommands.Add(new(item.Call.Target, timelineStart, timelineEnd, radians));
                        continue;
                    }

                    float localEnd = item.Time + validDuration;
                    foreach (var interval in channelClock.TimelineIntervalsForLocalRange(item.Time, localEnd))
                    {
                        float intervalRadians = validDuration <= 0
                            ? radians
                            : radians * interval.LocalDuration / validDuration;
                        rotationCommands.Add(new(item.Call.Target, interval.TimelineStart,
                            interval.TimelineEnd, intervalRadians));
                    }
                }
            }
        }
    }

    private static (CinematicScriptOwner Owner, CinematicEmbeddedEventTrackList List)[]
        FindPreviewableSceneEventOwners(AnimationClip sourceClip, WorldData world, SceneDocument document,
            IReadOnlyDictionary<uint, CinematicEmbeddedEventTrackList[]> sceneLists)
    {
        var activeWorldKeys = ResolveActiveWorldKeys(sourceClip, document);
        var ownersById = document.CinematicScriptOwners.Values.ToDictionary(owner => owner.Id);
        return sceneLists.Values.SelectMany(lists => lists)
            .Where(list => activeWorldKeys.Contains(list.WorldKey)
                && world.Objects.Any(sceneObject => sceneObject.GaoKey == list.OwnerKey))
            .Where(list => ownersById.TryGetValue(new(list.WorldKey, list.OwnerKey), out var owner)
                && owner.SceneEventsKey is > 2 and < uint.MaxValue
                && owner.SceneEventsKey == list.EventKey)
            .Select(list => (Owner: ownersById[new(list.WorldKey, list.OwnerKey)], List: list))
            .Where(item => item.List.Tracks.Length > 0)
            .DistinctBy(item => item.Owner.Id)
            .OrderBy(item => item.Owner.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static HashSet<uint> ResolveActiveWorldKeys(AnimationClip sourceClip, SceneDocument document)
    {
        var worldKeys = new HashSet<uint> { sourceClip.CinematicWorldKey };
        var cinematicWorld = document.Worlds.FirstOrDefault(world => world.Key == sourceClip.CinematicWorldKey);
        if (cinematicWorld == null || !cinematicWorld.Name.EndsWith("_cine", StringComparison.OrdinalIgnoreCase))
            return worldKeys;

        string environmentName = cinematicWorld.Name[..^"_cine".Length];
        var environmentWorld = document.Worlds.FirstOrDefault(world =>
            world.Name.Equals(environmentName, StringComparison.OrdinalIgnoreCase));
        if (environmentWorld != null) worldKeys.Add(environmentWorld.Key);
        return worldKeys;
    }

    private static bool TrySelectChannels(CinematicLiteralCall call, uint worldKey,
        IReadOnlyDictionary<uint, CinematicEmbeddedEventTrackList[]> sceneLists,
        out CinematicEmbeddedEventTrackList list, out CinematicEmbeddedEventTrack[] tracks,
        out CinematicEventRouteFailure failure)
    {
        list = null!;
        tracks = [];
        failure = CinematicEventRouteFailure.None;
        string function = CinematicEngineFunctionCatalog.NameFor(call.Selector);
        bool allChannels = function == "TRAPlayAll";
        if (function != "TRAPlay" && !allChannels) return false;
        if (!sceneLists.TryGetValue(call.Target, out var candidates))
        {
            failure = CinematicEventRouteFailure.MissingSceneEventList;
            return false;
        }

        var localCandidates = candidates.Where(candidate => candidate.WorldKey == worldKey).ToArray();
        CinematicEmbeddedEventTrackList[] resolvedCandidates;
        if (localCandidates.Length > 1)
        {
            failure = CinematicEventRouteFailure.AmbiguousSceneEventList;
            return false;
        }
        if (localCandidates.Length == 1)
            resolvedCandidates = localCandidates;
        else
        {
            int worldCandidateCount = candidates.Select(candidate => candidate.WorldKey).Distinct().Count();
            if (worldCandidateCount > 1)
            {
                failure = CinematicEventRouteFailure.AmbiguousWorldScope;
                return false;
            }
            if (candidates.Length != 1)
            {
                failure = CinematicEventRouteFailure.AmbiguousSceneEventList;
                return false;
            }
            resolvedCandidates = candidates;
        }

        list = resolvedCandidates[0];
        if (allChannels)
        {
            tracks = list.Tracks;
            if (tracks.Length > 0) return true;
            failure = CinematicEventRouteFailure.EmptySceneEventList;
            return false;
        }
        if (call.Arguments.Length == 0)
        {
            failure = CinematicEventRouteFailure.MissingChannelArgument;
            return false;
        }

        int requestedIndex = unchecked((int)call.Arguments[0]);
        tracks = list.Tracks.Where(track => track.Index == requestedIndex).ToArray();
        if (tracks.Length > 0) return true;
        failure = CinematicEventRouteFailure.MissingChannel;
        return false;
    }

    private static CinematicCallbackSourceBinding BindCallbackSource(CinematicScriptOwner owner,
        string callbackPath)
    {
        if (owner.CallbackSlots == null)
            return new(CinematicCallbackSourceBindingStatus.SlotTableUnavailable,
                null, null, callbackPath);

        var matchingSlots = owner.CallbackSlots.Where(slot => slot.FunctionName is { Length: > 0 } name
                && (callbackPath.Equals(name, StringComparison.Ordinal)
                    || callbackPath.StartsWith(name + " -[", StringComparison.Ordinal)))
            .ToArray();
        if (matchingSlots.Length == 1)
        {
            var slot = matchingSlots[0];
            return new(CinematicCallbackSourceBindingStatus.Bound, slot.Index,
                slot.FunctionName, callbackPath);
        }

        return new(matchingSlots.Length == 0
                ? CinematicCallbackSourceBindingStatus.NoMatchingSlot
                : CinematicCallbackSourceBindingStatus.AmbiguousSlot,
            null, null, callbackPath);
    }

    private static CinematicTrackPlaybackClock? CreatePlaybackClock(
        IReadOnlyList<(float Time, CinematicLiteralCall Call)> commands,
        CinematicEmbeddedEventTrackList list, CinematicEmbeddedEventTrack channel,
        float startTime, float timelineDuration, bool createWithoutInterruption = false)
    {
        var selectedTrackIndices = new HashSet<int> { channel.Index };
        var lifecycleCommands = commands
            .Where(item => item.Time >= startTime && item.Call.Target == list.OwnerKey
                && TryGetAffectedTracks(item.Call, selectedTrackIndices, list.Tracks, out _))
            .Select(item =>
            {
                string operation = CinematicEngineFunctionCatalog.NameFor(item.Call.Selector);
                float? value = operation == "TRASetTime" && item.Call.Arguments.Length > 0
                    ? BitConverter.UInt32BitsToSingle(item.Call.Arguments[0])
                    : null;
                return new CinematicTrackPlaybackCommand(item.Time, operation, value);
            })
            .Where(item => (item.Operation is "TRAPlay" or "TRAPlayAll"
                or "TRAStop" or "TRAStopAll" or "TRAPause" or "TRAPauseAll"
                or "TRAUnPause" or "TRAUnPauseAll" or "TRASetEnd"
                or "TRAReinit" or "TRAReinitAll" or "TRASetTime"
                ) && (item.Operation != "TRASetTime"
                    || item.Value is >= 0 and <= CinematicTrackPlaybackClock.MaximumUnwrappedSetTimeSeconds
                        && float.IsFinite(item.Value.Value)))
            .ToArray();
        bool hasInterruption = lifecycleCommands.Any(item => item.Operation is "TRAStop" or "TRAStopAll"
            or "TRAPause" or "TRAPauseAll" or "TRAUnPause" or "TRAUnPauseAll"
            or "TRASetEnd" or "TRAReinit" or "TRAReinitAll" or "TRASetTime"
            || item.Operation is "TRAPlay" or "TRAPlayAll"
                && item.Time >= startTime + channel.Duration);
        bool autoLoops = (channel.ChannelFlags & CinematicEventTrackFlags.AutoLoop) != 0;
        if (!hasInterruption && !autoLoops && !createWithoutInterruption) return null;

        float finalEventStart = channel.SourceTrack?.Keys.LastOrDefault()?.Time ?? channel.Duration;
        return CinematicTrackPlaybackClock.FromCommands(startTime, Math.Max(0, channel.Duration),
            timelineDuration, channel.ChannelFlags, lifecycleCommands, finalEventStart);
    }

    private static IEnumerable<float> MapChannelCommandTimes(float localTime, float startTime,
        CinematicTrackPlaybackClock? playbackClock)
    {
        if (playbackClock == null)
        {
            yield return startTime + localTime;
            yield break;
        }

        foreach (float timelineTime in playbackClock.TimelineTimesForLocal(localTime))
            yield return timelineTime;
    }

    private static bool TryGetAffectedTracks(CinematicLiteralCall call, HashSet<int> selectedIndices,
        CinematicEmbeddedEventTrack[] availableTracks, out CinematicEmbeddedEventTrack[] affectedTracks)
    {
        string function = CinematicEngineFunctionCatalog.NameFor(call.Selector);
        bool allChannels = function is "TRAPlayAll" or "TRAStopAll" or "TRAPauseAll" or "TRAUnPauseAll"
            or "TRAReinitAll" or "TRASetTime";
        bool oneChannel = function is "TRAPlay" or "TRAStop" or "TRAPause" or "TRAUnPause"
            or "TRAReinit" or "TRASetEnd";
        if (function == "TRASetTime" && call.Arguments.Length == 0)
        {
            affectedTracks = [];
            return false;
        }
        if (allChannels)
        {
            affectedTracks = availableTracks.Where(track => selectedIndices.Contains(track.Index)).ToArray();
            return affectedTracks.Length > 0;
        }
        if (!oneChannel || call.Arguments.Length == 0)
        {
            affectedTracks = [];
            return false;
        }

        int requestedIndex = unchecked((int)call.Arguments[0]);
        affectedTracks = availableTracks.Where(track => track.Index == requestedIndex
            && selectedIndices.Contains(track.Index)).ToArray();
        return affectedTracks.Length > 0;
    }

    private static AnimationTrack ShiftToCinematicTime(AnimationTrack sourceTrack,
        uint targetKey, float startTime, CinematicTrackPlaybackClock? playbackClock,
        bool usesFlashReference) => new()
    {
        Target = targetKey,
        Bone = sourceTrack.Bone,
        ChannelFlags = sourceTrack.ChannelFlags,
        HeaderWord = sourceTrack.HeaderWord,
        UsesFlashReference = usesFlashReference,
        CinematicPlaybackClock = playbackClock,
        Keys = sourceTrack.Keys.Select(key => new AnimationKey
        {
            Time = playbackClock == null ? startTime + key.Time : key.Time,
            Duration = key.Duration,
            Position = key.Position,
            Scale = key.Scale,
            Control1 = key.Control1,
            Control2 = key.Control2,
            Rotation = key.Rotation,
            Flags = key.Flags,
            Format = key.Format,
            FlashCompressedRotation = key.FlashCompressedRotation,
            TimeCurve = key.TimeCurve,
            OpaquePayloads = [.. key.OpaquePayloads]
        }).ToList()
    };

    internal static bool UsesFlashReference(CinematicEmbeddedEventTrackList list,
        CinematicEmbeddedEventTrack currentChannel, uint currentTargetKey)
    {
        // Iris EVE_b_Track_UseFlash scans the current list through the current channel
        // and matches the current GO. A null channel target resolves to the list owner.
        return list.Tracks
            .Where(channel => channel.Index <= currentChannel.Index
                && (channel.TargetKey == 0 ? list.OwnerKey : channel.TargetKey) == currentTargetKey)
            .Any(channel => AnimationReader.FirstKeyEnablesFlash(channel.SourceTrack));
    }
}

internal sealed record CinematicNestedEventTimeline(AnimationTrack[] TransformTracks,
    CinematicScriptOwnerId[] CallbackSceneEventOwners,
    CinematicScriptOwnerId[] PreviewableSceneEventOwners,
    (float Time, CinematicLiteralCall Call)[] Commands,
    CinematicLocalRotationCommand[] RotationCommands)
{
    internal (float Time, CinematicTimelineMessage Message)[] Messages { get; init; } = [];
    internal CinematicOwnerExecutionLedgerEntry[] OwnerExecutionLedger { get; init; } = [];
    internal CinematicOwnerChannelExecution[] OwnerChannelExecutions { get; init; } = [];
    internal CinematicScheduledMessage[] ScheduledMessages { get; init; } = [];
    internal GameScriptMoveSegment[] MoveSegments { get; init; } = [];
    internal string[] MoveDiagnostics { get; init; } = [];
    internal CinematicEventRouteDiagnostic[] RouteDiagnostics { get; init; } = [];
}

internal enum CinematicEventRouteFailure
{
    None,
    MissingSceneEventList,
    AmbiguousWorldScope,
    AmbiguousSceneEventList,
    EmptySceneEventList,
    MissingChannelArgument,
    MissingChannel
}

internal sealed record CinematicEventRouteDiagnostic(float Time, uint SourceOwnerKey,
    uint SourceEventKey, int? SourceChannelIndex, uint TargetOwnerKey, string Operation,
    CinematicEventRouteFailure Failure);
