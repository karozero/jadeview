using System.Numerics;
using JadeView;

internal static class GameScriptMoveSemanticsChecks
{
    internal static void Verify(string outputDirectory)
    {
        VerifyEveRatio();
        VerifyAiDispatchIsNotFrameScaled();
        VerifyUnknownContextDoesNotApply();
        VerifyInvalidInputsRemainUnresolved();
        VerifyEveChannelAdapter();
        VerifyTimelineUsesClockIntervals();
        VerifyPlaybackAndGlbExport(outputDirectory);
        Console.WriteLine("PASS: shared OBJMove semantics distinguish EVE ratios, AI dispatch, and unknown context");
    }

    private static void VerifyEveRatio()
    {
        var command = Command(GameScriptExecutionContext.EveEvent, duration: 2);
        var halfway = GameScriptMoveSemantics.EvaluateEveProgress(command, 1);
        var complete = GameScriptMoveSemantics.EvaluateEveProgress(command, 2);
        var beyondEnd = GameScriptMoveSemantics.EvaluateEveProgress(command, 3);
        if (!halfway.IsResolved || halfway.Displacement != new Vector3(2, 3, 4)
            || !complete.IsResolved || complete.Displacement != new Vector3(4, 6, 8)
            || !beyondEnd.IsResolved || beyondEnd.Displacement != new Vector3(4, 6, 8))
            throw new InvalidDataException("EVE OBJMove must scale by elapsed event time / source duration and stop at the full source vector.");
    }

    private static void VerifyAiDispatchIsNotFrameScaled()
    {
        var command = Command(GameScriptExecutionContext.AiCallback, duration: null);
        var dispatch = GameScriptMoveSemantics.ResolveAiCallbackDispatch(command);
        if (!dispatch.IsResolved || dispatch.Displacement != new Vector3(4, 6, 8))
            throw new InvalidDataException("A non-EVE AI callback dispatch must resolve one full OBJMove vector, not a frame-delta velocity.");
    }

    private static void VerifyUnknownContextDoesNotApply()
    {
        var command = Command(GameScriptExecutionContext.Unknown, duration: 2);
        var eve = GameScriptMoveSemantics.EvaluateEveProgress(command, 1);
        var callback = GameScriptMoveSemantics.ResolveAiCallbackDispatch(command);
        if (eve.IsResolved || callback.IsResolved
            || eve.Status != GameScriptMoveResolutionStatus.UnknownExecutionContext
            || callback.Status != GameScriptMoveResolutionStatus.UnknownExecutionContext)
            throw new InvalidDataException("Unknown OBJMove execution context must stay unresolved for both EVE and AI consumers.");
    }

    private static void VerifyInvalidInputsRemainUnresolved()
    {
        var missingDuration = Command(GameScriptExecutionContext.EveEvent, duration: null);
        var invalidElapsed = GameScriptMoveSemantics.EvaluateEveProgress(
            Command(GameScriptExecutionContext.EveEvent, duration: 2), float.NaN);
        var zeroDuration = GameScriptMoveSemantics.EvaluateEveProgress(
            Command(GameScriptExecutionContext.EveEvent, duration: 0), 1);
        var missing = GameScriptMoveSemantics.EvaluateEveProgress(missingDuration, 1);
        var invalidTarget = GameScriptMoveSemantics.ResolveAiCallbackDispatch(
            Command(GameScriptExecutionContext.AiCallback, duration: null, target: uint.MaxValue));
        if (missing.Status != GameScriptMoveResolutionStatus.MissingOrInvalidEventDuration
            || invalidElapsed.Status != GameScriptMoveResolutionStatus.InvalidElapsedTime
            || zeroDuration.Status != GameScriptMoveResolutionStatus.MissingOrInvalidEventDuration
            || invalidTarget.Status != GameScriptMoveResolutionStatus.InvalidTarget)
            throw new InvalidDataException("Malformed OBJMove context, timing, or target must not produce an applied displacement.");
    }

    private static void VerifyEveChannelAdapter()
    {
        const uint targetKey = 0x400;
        const float sourceTime = .5f;
        var channel = new CinematicEmbeddedEventTrack(2, targetKey, -1, 0, 1, 1)
        {
            Calls = [(sourceTime, new(targetKey, 0x1FA4,
            [
                BitConverter.SingleToUInt32Bits(4),
                BitConverter.SingleToUInt32Bits(6),
                BitConverter.SingleToUInt32Bits(8)
            ]))],
            SourceTrack = new AnimationTrack
            {
                Keys = [new AnimationKey { Time = sourceTime, Duration = .75f }]
            }
        };
        var eventList = new CinematicEmbeddedEventTrackList(0x200, "owner", 0x10,
            "SceneEvents", 0x300, [channel]);

        GameScriptMoveCommand[] adapted = CinematicGameScriptEventAdapter.ReadMoveCommands(eventList, channel);
        if (adapted is not [{ } command]
            || command.Context != GameScriptExecutionContext.EveEvent
            || command.TargetKey != targetKey
            || command.WorldDisplacement != new Vector3(4, 6, 8)
            || command.EventDurationSeconds != .75f
            || command.Origin.WorldKey != 0x10
            || command.Origin.OwnerKey != 0x200
            || command.Origin.EventKey != 0x300
            || command.Origin.ChannelIndex != 2
            || command.Origin.SourceTimeSeconds != sourceTime)
            throw new InvalidDataException("The EVE adapter must preserve OBJMove target, world vector, source event duration, and owner/channel provenance.");

        var malformed = channel with
        {
            Calls = [(sourceTime, new(targetKey, 0x1FA4, [1, 2]))]
        };
        if (CinematicGameScriptEventAdapter.ReadMoveCommands(eventList, malformed).Length != 0)
            throw new InvalidDataException("Malformed OBJMove argument layouts must be left out of the typed EVE semantic model.");

        var repeatedDuration = channel with
        {
            SourceTrack = new AnimationTrack
            {
                Keys =
                [
                    new AnimationKey { Time = sourceTime, Duration = .75f },
                    new AnimationKey { Time = sourceTime, Duration = .75f }
                ]
            }
        };
        if (CinematicGameScriptEventAdapter.ReadMoveCommands(eventList, repeatedDuration)
                .Single().EventDurationSeconds != .75f)
            throw new InvalidDataException("Repeated source keys with one distinct valid duration must preserve that duration.");

        foreach (float invalidDuration in new[] { 0f, float.NaN })
        {
            var mixedValidity = channel with
            {
                SourceTrack = new AnimationTrack
                {
                    Keys = [new() { Time = sourceTime, Duration = .75f },
                        new() { Time = sourceTime, Duration = invalidDuration }]
                }
            };
            if (CinematicGameScriptEventAdapter.ReadMoveCommands(eventList, mixedValidity)
                    .Single().EventDurationSeconds != null)
                throw new InvalidDataException("An invalid colliding source duration must not be discarded to select a valid one.");
        }

        var conflictingDuration = channel with
        {
            SourceTrack = new AnimationTrack
            {
                Keys =
                [
                    new AnimationKey { Time = sourceTime, Duration = .75f },
                    new AnimationKey { Time = sourceTime, Duration = 1.25f }
                ]
            }
        };
        var durationDiagnostics = new List<string>();
        if (CinematicGameScriptEventAdapter.ReadMoveCommands(eventList, conflictingDuration)
                .Single().EventDurationSeconds != null
            || CinematicGameScriptEventAdapter.ReadMoveSegments(eventList, conflictingDuration,
                0, 2, null, durationDiagnostics).Length != 0
            || durationDiagnostics.Count != 1
            || !durationDiagnostics[0].Contains("ambiguous", StringComparison.Ordinal))
            throw new InvalidDataException("Same-time source keys with conflicting valid durations must leave OBJMove unresolved and explain why.");
    }

    private static void VerifyTimelineUsesClockIntervals()
    {
        const uint targetKey = 0x400;
        var channel = new CinematicEmbeddedEventTrack(2, targetKey, -1,
            CinematicEventTrackFlags.AutoLoop, 1, 1)
        {
            Calls = [(.25f, new(targetKey, 0x1FA4,
            [
                BitConverter.SingleToUInt32Bits(4),
                BitConverter.SingleToUInt32Bits(6),
                BitConverter.SingleToUInt32Bits(8)
            ]))],
            SourceTrack = new AnimationTrack
            {
                Keys = [new AnimationKey { Time = .25f, Duration = .5f }]
            }
        };
        var eventList = new CinematicEmbeddedEventTrackList(0x200, "owner", 0x10,
            "SceneEvents", 0x300, [channel]);
        var loopClock = new CinematicTrackPlaybackClock(0, 1, 1.75f,
            CinematicEventTrackFlags.AutoLoop, []);
        GameScriptMoveSegment[] segments = CinematicGameScriptEventAdapter.ReadMoveSegments(
            eventList, channel, 0, 1.75f, loopClock);
        if (segments.Length != 2 || segments[0].TimelineStartSeconds != .25f
            || segments[1].TimelineStartSeconds != 1.25f)
            throw new InvalidDataException("Scheduled EVE OBJMove segments must follow source event ranges across AutoLoop clock runs.");

        var target = new SceneObject
        {
            GaoKey = targetKey,
            Name = "unparented prop",
            Transform = new TransformData { Translation = new(10, 20, 30) }
        };
        var world = new WorldData();
        world.Objects.Add(target);
        var timeline = new CinematicGameScriptMoveTimeline(world, segments, []);
        Matrix4x4 halfwayThroughSecondLoop = timeline.Apply(target,
            AnimationMath.Trs(target.Transform), 1.5f);
        if (Vector3.Distance(halfwayThroughSecondLoop.Translation, new(16, 29, 42)) > .001f)
            throw new InvalidDataException("The EVE move timeline must accumulate a completed loop and partial current-loop displacement as a world-space offset.");

        var absolutePositionReset = new CinematicLiteralCall(targetKey, 0x1FA6, [0, 0, 0]);
        var resetTimeline = new CinematicGameScriptMoveTimeline(world, segments,
            [(1.5f, absolutePositionReset)]);
        Matrix4x4 atReset = resetTimeline.Apply(target, AnimationMath.Trs(target.Transform), 1.5f);
        Matrix4x4 afterResetLoop = resetTimeline.Apply(target, AnimationMath.Trs(target.Transform), 1.75f);
        if (Vector3.Distance(atReset.Translation, new(10, 20, 30)) > .001f
            || Vector3.Distance(afterResetLoop.Translation, new(12, 23, 34)) > .001f)
            throw new InvalidDataException("An absolute OBJSetPos setter must reset prior move accumulation while allowing later EVE displacement.");

        var ambiguous = new CinematicGameScriptMoveTimeline(world, segments,
            [(.25f, absolutePositionReset)]);
        if (ambiguous.Targets.Count != 0
            || !ambiguous.Diagnostics.Any(message => message.Contains("execution order is unresolved", StringComparison.Ordinal)))
            throw new InvalidDataException("Coincident OBJMove and absolute position set must be withheld when source execution order is unresolved.");

        var absoluteTrack = new CinematicGameScriptMoveTimeline(world, segments, [],
            new HashSet<uint> { targetKey });
        if (absoluteTrack.Targets.Count != 0
            || !absoluteTrack.Diagnostics.Any(message => message.Contains("absolute source position track", StringComparison.Ordinal)))
            throw new InvalidDataException("A prop with an absolute source position track must not receive an additive OBJMove without a proven command ordering.");

        target.ParentId = "unresolved-parent-reference";
        var unresolvedParent = new CinematicGameScriptMoveTimeline(world, segments, []);
        if (unresolvedParent.Targets.Count != 0
            || !unresolvedParent.Diagnostics.Any(message => message.Contains("hierarchy-bound", StringComparison.Ordinal)))
            throw new InvalidDataException("A non-empty but unparsable parent reference must remain unresolved instead of being treated as a root prop.");
        target.ParentId = null;
    }

    private static void VerifyPlaybackAndGlbExport(string outputDirectory)
    {
        const uint worldKey = 0x10;
        const uint ownerKey = 0x200;
        const uint targetKey = 0x400;
        const float channelStart = .5f;
        const float moveStart = .25f;
        const float moveDuration = .5f;
        var world = new WorldData { Key = worldKey };
        world.Objects.Add(new SceneObject { GaoKey = ownerKey, Name = $"object {ownerKey:X8}" });
        var target = new SceneObject
        {
            GaoKey = targetKey,
            Name = $"object {targetKey:X8}",
            Transform = new TransformData { Translation = new(10, 20, 30) }
        };
        world.Objects.Add(target);
        var clip = new AnimationClip
        {
            Key = 0x500,
            Name = "EVE OBJMove export",
            Duration = 2,
            CinematicWorldKey = worldKey,
            Cinematic = true,
            SceneMotion = true,
            Tracks = [new AnimationTrack
            {
                Bone = -1,
                Target = ownerKey,
                Keys = [CinematicTestEvents.Call(channelStart, ownerKey, 0x3B64, 0)]
            }]
        };
        world.CinematicClips.Add(clip.Key, clip);
        var channel = new CinematicEmbeddedEventTrack(0, targetKey, -1, 0, 1, 1)
        {
            Calls = [(moveStart, new(targetKey, 0x1FA4,
            [
                BitConverter.SingleToUInt32Bits(4),
                BitConverter.SingleToUInt32Bits(6),
                BitConverter.SingleToUInt32Bits(8)
            ]))],
            SourceTrack = new AnimationTrack
            {
                Bone = -1,
                Target = targetKey,
                Keys = [new AnimationKey { Time = moveStart, Duration = moveDuration }]
            }
        };
        var eventList = new CinematicEmbeddedEventTrackList(ownerKey, "move owner", worldKey,
            "SceneEvents", 0x600, [channel]);
        var document = new SceneDocument();
        document.Worlds.Add(world);
        document.CinematicClips.Add(clip.Key, clip);
        document.CinematicEmbeddedEventTracks[(worldKey, ownerKey)] = [eventList];

        var playback = CinematicPlayback.Discover(document, world, new()).Single();
        playback.Activate();
        playback.Seek(channelStart + moveStart + moveDuration / 2);
        Matrix4x4 previewPose = target.PreviewMatrix
            ?? throw new InvalidDataException("The EVE move target was not included in cinematic preview.");
        if (Vector3.Distance(previewPose.Translation, new(12, 23, 34)) > .001f)
            throw new InvalidDataException("Cinematic preview must apply the halfway EVE OBJMove displacement to the unparented prop.");
        playback.Deactivate();

        string path = Path.Combine(outputDirectory, "game-script-eve-object-move.glb");
        SceneExporter.Export(path, world, new(), new()
        {
            Animations = true,
            UnknownObjects = true,
            CinematicClipKey = clip.Key,
            CinematicDocument = document
        });
        using var glb = new GlbReader(path);
        int targetNode = Enumerable.Range(0, glb.Root.GetProperty("nodes").GetArrayLength())
            .Single(index => glb.Root.GetProperty("nodes")[index].GetProperty("name").GetString() == target.Name);
        int animationIndex = Enumerable.Range(0, glb.Root.GetProperty("animations").GetArrayLength())
            .Single(index => glb.Root.GetProperty("animations")[index].GetProperty("name").GetString() == clip.Name);
        Matrix4x4 expectedHalfway = AnimationMath.ToRender(Matrix4x4.CreateTranslation(12, 23, 34));
        Matrix4x4 exportedHalfway = glb.World(targetNode, animationIndex,
            channelStart + moveStart + moveDuration / 2);
        Matrix4x4 expectedEnd = AnimationMath.ToRender(Matrix4x4.CreateTranslation(14, 26, 38));
        Matrix4x4 exportedEnd = glb.World(targetNode, animationIndex,
            channelStart + moveStart + moveDuration);
        if (Vector3.Distance(expectedHalfway.Translation, exportedHalfway.Translation) > .001f
            || Vector3.Distance(expectedEnd.Translation, exportedEnd.Translation) > .001f)
            throw new InvalidDataException("GLB export must use the same EVE OBJMove pose as preview at partial and completed event times.");
    }

    private static GameScriptMoveCommand Command(GameScriptExecutionContext context,
        float? duration, uint target = 0x100) => new(target, new(4, 6, 8), context,
            duration, new(0x10, 0x20, 0x30, 2, .25f));
}
