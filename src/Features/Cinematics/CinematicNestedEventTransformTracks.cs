namespace JadeView;

internal static class CinematicNestedEventTransformTracks
{
    private const int MaximumDepth = 8;
    private const int MaximumScheduledTracks = 512;

    internal static AnimationTrack[] Read(AnimationClip sourceClip, WorldData world, SceneDocument document) =>
        ReadTimeline(sourceClip, world, document).TransformTracks;

    internal static CinematicNestedEventTimeline ReadTimeline(AnimationClip sourceClip, WorldData world,
        SceneDocument document, IReadOnlySet<CinematicScriptOwnerId>? disabledCallbackOwners = null,
        IReadOnlySet<CinematicScriptOwnerId>? previewedSceneEventOwners = null)
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
        var rotationCommands = new List<CinematicLocalRotationCommand>();
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
            if (ownerHasExplicitPreviewControl || callbackOwnerWasDisabled) continue;

            Schedule(list, channel, 0, 0,
                new HashSet<CinematicNestedEventTrackIdentity>(), commands);
        }

        foreach (var (owner, starts) in callbackStartsByOwner)
        {
            if (disabledCallbackOwners?.Contains(owner.Id) == true) continue;

            foreach (var invocation in starts)
            foreach (var channel in invocation.Tracks)
                Schedule(invocation.EventList, channel, 0, 0,
                    new HashSet<CinematicNestedEventTrackIdentity>(), commands);
        }

        foreach (var (owner, list) in previewableOwners.Where(item =>
                     previewedSceneEventOwners?.Contains(item.Owner.Id) == true))
        foreach (var channel in list.Tracks)
            Schedule(list, channel, 0, 0,
                new HashSet<CinematicNestedEventTrackIdentity>(), commands);

        foreach (var (startTime, call) in commands)
        {
            if (!TrySelectChannels(call, world.Key, sceneLists, out var list, out var tracks)) continue;

            foreach (var channel in tracks)
                Schedule(list, channel, startTime, 0,
                    new HashSet<CinematicNestedEventTrackIdentity>(), commands);
        }

        rotationCommands.AddRange(CinematicLocalRotationTimeline.ReadSourceCommands(sourceClip));
        return new(transforms.ToArray(), callbackOwners.Distinct().ToArray(),
            previewableOwners.Select(item => item.Owner.Id).Distinct().ToArray(), eventCommands.DistinctBy(item =>
            (item.Time, item.Call.Target, item.Call.Selector, Arguments: string.Join(",", item.Call.Arguments)))
            .OrderBy(item => item.Time).ToArray(), rotationCommands.ToArray());

        void Schedule(CinematicEmbeddedEventTrackList list, CinematicEmbeddedEventTrack channel,
            float startTime, int depth, HashSet<CinematicNestedEventTrackIdentity> ancestry,
            IReadOnlyList<(float Time, CinematicLiteralCall Call)> lifecycleCommands)
        {
            bool invalidScheduleRange = !float.IsFinite(startTime) || startTime < 0
                || startTime > sourceClip.Duration || !float.IsFinite(channel.Duration) || channel.Duration < 0;
            if (depth >= MaximumDepth || scheduledTrackCount >= MaximumScheduledTracks || invalidScheduleRange) return;
            var identity = new CinematicNestedEventTrackIdentity(list.WorldKey, list.OwnerKey,
                list.EventKey, channel.Index);
            if (ancestry.Contains(identity) || !activeChannels.Add(identity)) return;
            scheduledTrackCount++;

            var parentPlaybackClock = CreatePlaybackClock(lifecycleCommands, list, channel,
                startTime, sourceClip.Duration);
            var absoluteChannelCommands = channel.Calls
                .SelectMany(item => MapChannelCommandTimes(item.Time, startTime, parentPlaybackClock)
                    .Select(time => (Time: time, item.Call))).ToArray();
            var channelLifecycleCommands = lifecycleCommands.Concat(absoluteChannelCommands).ToArray();
            CinematicTrackPlaybackClock? playbackClock = CreatePlaybackClock(channelLifecycleCommands, list, channel,
                startTime, sourceClip.Duration);
            AddLocalRotationCommands(channel, startTime, playbackClock);

            eventCommands.AddRange(absoluteChannelCommands);

            uint targetKey = channel.TargetKey == 0 ? list.OwnerKey : channel.TargetKey;
            bool targetLoaded = targetKey is > 0 and < uint.MaxValue
                && world.Objects.Any(sceneObject => sceneObject.GaoKey == targetKey);
            if (targetLoaded && CinematicEventTrackPreview.HasTransformData(channel.SourceTrack))
                transforms.Add(ShiftToCinematicTime(channel.SourceTrack!, targetKey, startTime, playbackClock));

            var nextAncestry = new HashSet<CinematicNestedEventTrackIdentity>(ancestry) { identity };
            foreach (var (nestedStartTime, nestedCall) in absoluteChannelCommands)
            {
                if (!TrySelectChannels(nestedCall, list.WorldKey, sceneLists, out var nestedList,
                        out var nestedChannels))
                    continue;
                foreach (var nestedChannel in nestedChannels)
                    Schedule(nestedList, nestedChannel, nestedStartTime, depth + 1,
                        nextAncestry, channelLifecycleCommands);
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
        out CinematicEmbeddedEventTrackList list, out CinematicEmbeddedEventTrack[] tracks)
    {
        list = null!;
        tracks = [];
        string function = CinematicEngineFunctionCatalog.NameFor(call.Selector);
        bool allChannels = function == "TRAPlayAll";
        if (function != "TRAPlay" && !allChannels) return false;
        if (!sceneLists.TryGetValue(call.Target, out var candidates)) return false;

        var localCandidates = candidates.Where(candidate => candidate.WorldKey == worldKey).ToArray();
        var resolvedCandidates = localCandidates.Length > 0
            ? localCandidates
            : candidates.Select(candidate => candidate.WorldKey).Distinct().Count() == 1
                ? candidates
                : [];
        if (resolvedCandidates.Length != 1) return false;

        list = resolvedCandidates[0];
        if (allChannels)
        {
            tracks = list.Tracks;
            return true;
        }
        if (call.Arguments.Length == 0) return false;

        int requestedIndex = unchecked((int)call.Arguments[0]);
        tracks = list.Tracks.Where(track => track.Index == requestedIndex).ToArray();
        return tracks.Length > 0;
    }

    private static CinematicTrackPlaybackClock? CreatePlaybackClock(
        IReadOnlyList<(float Time, CinematicLiteralCall Call)> commands,
        CinematicEmbeddedEventTrackList list, CinematicEmbeddedEventTrack channel,
        float startTime, float timelineDuration)
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
        if (!hasInterruption && !autoLoops) return null;

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
        uint targetKey, float startTime, CinematicTrackPlaybackClock? playbackClock) => new()
    {
        Target = targetKey,
        Bone = sourceTrack.Bone,
        ChannelFlags = sourceTrack.ChannelFlags,
        HeaderWord = sourceTrack.HeaderWord,
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
            TimeCurve = key.TimeCurve,
            OpaquePayloads = [.. key.OpaquePayloads]
        }).ToList()
    };
}

internal sealed record CinematicNestedEventTimeline(AnimationTrack[] TransformTracks,
    CinematicScriptOwnerId[] CallbackSceneEventOwners,
    CinematicScriptOwnerId[] PreviewableSceneEventOwners,
    (float Time, CinematicLiteralCall Call)[] Commands,
    CinematicLocalRotationCommand[] RotationCommands);
