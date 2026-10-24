namespace JadeView;

internal sealed record CinematicNleTimelineLane(string Name, CinematicNleLaneKind Kind,
    CinematicNleTimelineItem[] Items);

internal sealed record CinematicNleTimelineItem(float Start, float End, string Label, string Details);

internal enum CinematicNleLaneKind
{
    Animation,
    BodyAction,
    Script,
    Message,
    Audio
}

internal enum CinematicScriptOwnerCategory
{
    All,
    ClipLinked,
    CinematicWorld,
    ReferencedWorld
}

internal static class CinematicNleTimelineBuilder
{
    internal static CinematicNleTimelineLane[] Build(CinematicPlayback playback, WorldData world,
        SceneDocument document)
    {
        var lanes = new List<CinematicNleTimelineLane>();
        AnimationClip clip = playback.SourceClip;
        clip.EnsureLoaded();
        var queueReaderResolver = new CinematicMessageQueueReaderResolver(document.CinematicScriptOwners.Values);

        var animationLanes = clip.Tracks
            .Where(track => track.Keys.Count > 0)
            .GroupBy(track => track.Target)
            .Select(group => new CinematicNleTimelineLane(
                ResolveObjectName(group.Key, world), CinematicNleLaneKind.Animation,
                group.Select(track => new CinematicNleTimelineItem(
                    track.Keys.Min(key => key.Time),
                    Math.Max(track.Keys.Max(key => key.Time), track.Keys.Min(key => key.Time)),
                    track.Bone < 0 ? "Transform" : $"Bone {track.Bone}",
                    $"Target {group.Key:X8} · {track.Keys.Count} keys · channel flags 0x{track.ChannelFlags:X4}"))
                    .ToArray()))
            .OrderBy(lane => lane.Name, StringComparer.OrdinalIgnoreCase);
        lanes.AddRange(animationLanes);

        foreach (var (actor, bodyTimeline) in playback.BodyTimelines)
        {
            var bodyItems = bodyTimeline.TimelineSegments(playback.Duration)
                .Select(segment => new CinematicNleTimelineItem(segment.Start, segment.End,
                    segment.Label, $"{actor.Root.Name} · {segment.Label}"))
                .ToArray();
            if (bodyItems.Length > 0)
                lanes.Add(new($"{actor.Root.Name} · body", CinematicNleLaneKind.BodyAction, bodyItems));
        }

        AddScriptLanes(lanes, playback, world, document);
        AddEveTransitionLane(lanes, playback);
        AddUnresolvedRouteLane(lanes, playback);

        var scheduledMessages = playback.ScheduledMessages
            .GroupBy(message => (message.TimelineTime, message.Message))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var messageItems = playback.ScriptMessages
            .Select(item =>
            {
                string details = $"Execution {item.Message.ExecutionObjectKey:X8} · GAO {item.Message.Gao:X8} · GAO2 {item.Message.Gao2:X8}";
                if (scheduledMessages.TryGetValue((item.Time, item.Message), out var scheduledRoutes))
                {
                    string[] routeDetails = scheduledRoutes.Select(scheduled =>
                    {
                        string state = scheduled.ChannelState is { } snapshot
                            ? $" · EVE {(snapshot.Running ? "running" : "stopped")}{(snapshot.Paused ? ", paused" : "")}"
                                + $" · local {snapshot.LocalTime:F3}s · run {snapshot.RunIndex}"
                            : " · EVE state unavailable";
                        return $"{scheduled.PreviewRoute} · owner {scheduled.EventListOwnerKey:X8}"
                            + $" · event {scheduled.EventKey:X8} · channel {scheduled.ChannelIndex}"
                            + $" · source {scheduled.ChannelSourceTime:F3}s{state}"
                            + (scheduled.IrisQueueOwnerCandidateKey is { } queueOwner
                                ? $" · Iris queue candidate {queueOwner:X8}"
                                : " · Iris queue owner unresolved")
                            + $" · {queueReaderResolver.DescribePotentialReaders(scheduled)}"
                            + " · delivery/runtime not evaluated";
                    }).Distinct(StringComparer.Ordinal).ToArray();
                    details += " · " + string.Join(" | ", routeDetails);
                }
                return new CinematicNleTimelineItem(item.Time, item.Time,
                    $"Message {item.Message.Type}", details);
            })
            .ToArray();
        if (messageItems.Length > 0)
            lanes.Add(new("Messages", CinematicNleLaneKind.Message, messageItems));

        var audioItems = playback.AudioCues
            .Select(cue => new CinematicNleTimelineItem(cue.Start,
                Math.Max(cue.Start, cue.End), cue.Label ?? $"Sound {cue.SoundKey:X8}",
                $"Owner {cue.OwnerKey:X8} · modifier {cue.ModifierId} · {cue.Volume:P0} volume"))
            .ToArray();
        if (audioItems.Length > 0)
            lanes.Add(new("Audio cues", CinematicNleLaneKind.Audio, audioItems));

        return lanes.ToArray();
    }

    private static void AddScriptLanes(List<CinematicNleTimelineLane> lanes,
        CinematicPlayback playback, WorldData world, SceneDocument document)
    {
        var sourceEventTracks = document.CinematicEmbeddedEventTracks.Values
            .SelectMany(lists => lists)
            .Where(list => list.LinkName.Equals("SceneEvents", StringComparison.OrdinalIgnoreCase))
            .SelectMany(list => list.Tracks.Select(track => new EventTrackSource(list, track)))
            .ToArray();
        var sourceClipCalls = playback.ScriptCalls.Select(item => item.Call)
            .ToHashSet(ReferenceEqualityComparer.Instance);
        var itemsByLane = new Dictionary<string, List<CinematicNleTimelineItem>>(StringComparer.Ordinal);

        foreach (var (time, call) in playback.ScriptStateCalls)
        {
            var item = new CinematicNleTimelineItem(time, time,
                CinematicEngineFunctionCatalog.NameFor(call.Selector),
                $"{call.Target:X8} · {CinematicEngineFunctionCatalog.Describe(call)}");
            if (sourceClipCalls.Contains(call))
            {
                AddItem("Clip script calls", item);
                continue;
            }

            EventTrackSource[] matchingSources = sourceEventTracks
                .Where(source => source.Track.Calls.Any(entry => ReferenceEquals(entry.Call, call)))
                .ToArray();
            if (matchingSources.Length == 1)
            {
                EventTrackSource source = matchingSources[0];
                string ownerName = source.List.OwnerName;
                string targetName = ResolveObjectName(source.List.OwnerKey, world);
                AddItem($"{ownerName} · EVE {source.Track.Index}", item with
                {
                    Details = $"{targetName} · event {source.List.EventKey:X8} · {item.Details}"
                });
                continue;
            }

            if (matchingSources.Length > 1)
            {
                string sourceNames = string.Join(", ", matchingSources.Select(source =>
                    $"{source.List.OwnerName} EVE {source.Track.Index}").Distinct(StringComparer.Ordinal));
                AddItem("Shared / ambiguous EVE sources", item with
                {
                    Details = $"Matched {matchingSources.Length} serialized channel references ({sourceNames}); {item.Details}"
                });
                continue;
            }

            AddItem("Unattributed script calls", item);
        }

        foreach (var (laneName, items) in itemsByLane.OrderBy(pair => pair.Key,
                     StringComparer.OrdinalIgnoreCase))
            if (items.Count > 0)
                lanes.Add(new(laneName, CinematicNleLaneKind.Script, items.ToArray()));

        void AddItem(string laneName, CinematicNleTimelineItem item)
        {
            if (!itemsByLane.TryGetValue(laneName, out var laneItems))
                itemsByLane[laneName] = laneItems = [];
            laneItems.Add(item);
        }
    }

    private static void AddUnresolvedRouteLane(List<CinematicNleTimelineLane> lanes,
        CinematicPlayback playback)
    {
        var items = playback.EventRouteDiagnostics.Select(diagnostic =>
        {
            string source = diagnostic.SourceChannelIndex is { } channelIndex
                ? $"EVE {channelIndex} · owner {diagnostic.SourceOwnerKey:X8} · event {diagnostic.SourceEventKey:X8}"
                : $"clip event {diagnostic.SourceEventKey:X8}";
            return new CinematicNleTimelineItem(diagnostic.Time, diagnostic.Time,
                $"{diagnostic.Operation} → {diagnostic.TargetOwnerKey:X8}",
                $"{source} · {DescribeRouteFailure(diagnostic.Failure)}");
        }).ToArray();

        if (items.Length > 0)
            lanes.Add(new("Unresolved EVE routes", CinematicNleLaneKind.Script, items));
    }

    private static void AddEveTransitionLane(List<CinematicNleTimelineLane> lanes,
        CinematicPlayback playback)
    {
        var items = playback.OwnerChannelExecutions
            .SelectMany(execution => execution.Clock.Transitions
                .Where(transition => transition.Operation == "Start"
                    || transition.Before != transition.After)
                .Select(transition => new CinematicNleTimelineItem(transition.Time, transition.Time,
                    $"{transition.Operation} · EVE {execution.ChannelIndex}",
                    $"owner {execution.OwnerId.OwnerKey:X8} · event {execution.EventKey:X8}"
                    + $" · {execution.PreviewRoute} · {FormatState(transition.Before)} → {FormatState(transition.After)}"
                    + $" · {DescribeTransitionEvidence(transition.Operation)}")))
            .OrderBy(item => item.Start).ThenBy(item => item.Label, StringComparer.Ordinal)
            .ToArray();

        if (items.Length > 0)
            lanes.Add(new("EVE state transitions", CinematicNleLaneKind.Script, items));

        static string FormatState(CinematicTrackPlaybackSnapshot state) =>
            $"{(state.Running ? "running" : "stopped")}{(state.Paused ? ", paused" : "")}"
            + $" · local {state.LocalTime:F3}s · run {state.RunIndex}";
    }

    private static string DescribeTransitionEvidence(string operation) => operation switch
    {
        "Start" => "preview route start; live callback dispatch is unverified",
        "AutoLoop" => "derived from the serialized AutoLoop channel flag; runtime not observed",
        "AutoStop" => "derived from the serialized AutoStop channel flag; runtime not observed",
        _ => "mapped from decoded EVE/cinematic commands; live callback dispatch is unverified"
    };

    private static string DescribeRouteFailure(CinematicEventRouteFailure failure) => failure switch
    {
        CinematicEventRouteFailure.MissingSceneEventList => "no SceneEvents list is indexed for the target",
        CinematicEventRouteFailure.AmbiguousWorldScope => "target resolves to SceneEvents lists in multiple worlds",
        CinematicEventRouteFailure.AmbiguousSceneEventList => "multiple SceneEvents lists remain for the target",
        CinematicEventRouteFailure.EmptySceneEventList => "target SceneEvents list has no decoded channels",
        CinematicEventRouteFailure.MissingChannelArgument => "TRAPlay has no literal channel index",
        CinematicEventRouteFailure.MissingChannel => "requested channel index is absent from the resolved list",
        _ => "route could not be resolved"
    };

    private sealed record EventTrackSource(CinematicEmbeddedEventTrackList List,
        CinematicEmbeddedEventTrack Track);

    private static string ResolveObjectName(uint key, WorldData world) =>
        world.Objects.FirstOrDefault(sceneObject => sceneObject.GaoKey == key)?.Name ?? $"Target {key:X8}";
}
