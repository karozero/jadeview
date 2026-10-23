using System.Numerics;
using JadeView;

internal static class CinematicNestedEventTransformChecks
{
    internal static void Verify(string outputDirectory)
    {
        VerifyIndexedPlaybackUsesCinematicTime();
        VerifyNestedPlaybackAndPlayAll();
        VerifyNestedLocalRotationPlaybackAndExport(outputDirectory);
        VerifyNestedLifecycleUsesCinematicClock();
        VerifyNestedTrackFlags(outputDirectory);
        VerifyCallbackBoundSceneEvents(outputDirectory);
        VerifyRunningInitHonorsOwnerSelection();
        VerifyManualSceneEventOwnerPreview(outputDirectory);
        VerifyStopAndAmbiguousRoutesAreOmitted();
        VerifyPlaybackAndExport(outputDirectory);
        Console.WriteLine("PASS: explicit nested SceneEvents transform playback, offsets, stop gates and world resolution");
    }

    private static void VerifyIndexedPlaybackUsesCinematicTime()
    {
        var world = World(0x10, 0x100, 0x200);
        var document = new SceneDocument();
        var channel = TransformChannel(0, 0x200, 0, 3);
        AddList(document, List(0x100, 0x10, 0x150, [channel]));
        AnimationTrack[] tracks = Read(world, document, Clip(0x100, 1, 0x3B64, 0));

        bool shiftedKeys = tracks.Length == 1 && tracks[0].Target == 0x200
            && tracks[0].Keys[0].Time == 1 && tracks[0].Keys[0].Position == new Vector3(3, 0, 0);
        var baseline = new TransformData { Translation = new Vector3(9, 0, 0) };
        Matrix4x4 beforeStart = CinematicTransformSampler.Evaluate(baseline, tracks, .99f);
        Matrix4x4 atStart = CinematicTransformSampler.Evaluate(baseline, tracks, 1);
        if (!shiftedKeys || MathF.Abs(beforeStart.M41 - 9) > .001f || MathF.Abs(atStart.M41 - 3) > .001f)
            throw new InvalidDataException("TRAPlay must shift nested transform keys to the cinematic clock and leave the target unchanged before activation.");
    }

    private static void VerifyNestedPlaybackAndPlayAll()
    {
        var world = World(0x10, 0x100, 0x200, 0x300, 0x400);
        var document = new SceneDocument();
        var rootChannel = new CinematicEmbeddedEventTrack(0, 0, -1, 0, 1, 1)
        {
            Calls = [(0.5f, new(0x200, 0x3B64, [1]))]
        };
        var nestedChannels = new[]
        {
            new CinematicEmbeddedEventTrack(0, 0x300, -1, 0, 1, 1)
            {
                SourceTrack = TransformSource(2)
            },
            new CinematicEmbeddedEventTrack(1, 0x400, -1, 0, 1, 1)
            {
                SourceTrack = TransformSource(4)
            }
        };
        AddList(document, List(0x100, 0x10, 0x150, [rootChannel]));
        AddList(document, List(0x200, 0x10, 0x250, nestedChannels));
        AnimationTrack[] nested = Read(world, document, Clip(0x100, 1, 0x3B64, 0));
        bool nestedOffset = nested.Length == 1 && nested[0].Target == 0x400
            && nested[0].Keys[0].Time == 1.5f;

        AddList(document, List(0x100, 0x10, 0x151,
        [
            new CinematicEmbeddedEventTrack(0, 0x300, -1, 0, 1, 1) { SourceTrack = TransformSource(2) },
            new CinematicEmbeddedEventTrack(1, 0x400, -1, 0, 1, 1) { SourceTrack = TransformSource(4) },
            new CinematicEmbeddedEventTrack(2, 0x400, -1, 0, 1, 1)
        ]));
        AnimationTrack[] all = Read(world, document, Clip(0x100, 2, 0x3B60));
        if (!nestedOffset || all.Length != 2 || all.Select(track => track.Target).Order().SequenceEqual([0x300u, 0x400u]) == false)
            throw new InvalidDataException("Nested TRAPlay and TRAPlayAll must evaluate transform-bearing SceneEvents on their scheduled cinematic times.");
    }

    private static void VerifyNestedLocalRotationPlaybackAndExport(string outputDirectory)
    {
        const uint worldKey = 0x10;
        const uint ownerKey = 0x200;
        const uint targetKey = 0x300;
        const float eventStart = .75f;
        const float eventDuration = .75f;
        float angle = MathF.PI / 2;
        var world = World(worldKey, ownerKey, targetKey);
        var clip = new AnimationClip
        {
            Key = 0x500,
            Name = "nested local rotation",
            Duration = 3,
            CinematicWorldKey = worldKey,
            Cinematic = true,
            SceneMotion = true,
            Tracks = [new AnimationTrack
            {
                Bone = -1,
                Target = ownerKey,
                Keys = [CinematicTestEvents.Call(.5f, ownerKey, 0x3B64, 0)]
            }]
        };
        world.CinematicClips.Add(clip.Key, clip);
        var document = new SceneDocument();
        document.Worlds.Add(world);
        document.CinematicClips.Add(clip.Key, clip);
        AnimationKey rotationKey = CinematicTestEvents.Call(.25f, targetKey, 0x1FAA,
            BitConverter.SingleToUInt32Bits(angle));
        rotationKey.Duration = eventDuration;
        var rotationChannel = new CinematicEmbeddedEventTrack(0, targetKey, -1, 0, 1, 1)
        {
            Calls = [(.25f, new(targetKey, 0x1FAA, [BitConverter.SingleToUInt32Bits(angle)]))],
            SourceTrack = new AnimationTrack { Bone = -1, Keys = [rotationKey] }
        };
        AddList(document, List(ownerKey, worldKey, 0x250, [rotationChannel]));

        CinematicNestedEventTimeline timeline = CinematicNestedEventTransformTracks.ReadTimeline(
            clip, world, document);
        if (timeline.RotationCommands is not [{ TargetKey: targetKey } command]
            || MathF.Abs(command.Start - eventStart) > .001f
            || MathF.Abs(command.End - (eventStart + eventDuration)) > .001f
            || MathF.Abs(command.Radians - angle) > .001f)
            throw new InvalidDataException("A nested OBJRotateLocalY event lost its target, cinematic offset, angle, or key duration.");

        var playback = CinematicPlayback.Discover(document, world, new(), null).Single();
        playback.Activate();
        playback.Seek(eventStart + eventDuration / 2);
        Matrix4x4 previewPose = world.Objects.Single(item => item.GaoKey == targetKey).PreviewMatrix!.Value;
        if (MathF.Abs(previewPose.M11 - MathF.Cos(angle / 2)) > .001f
            || MathF.Abs(previewPose.M31 - MathF.Sin(angle / 2)) > .001f)
            throw new InvalidDataException("Nested OBJRotateLocalY playback did not evaluate at the authored source time.");

        string exportPath = Path.Combine(outputDirectory, "cinematic-nested-local-rotation.glb");
        SceneExporter.Export(exportPath, world, new(), new()
        {
            Animations = true,
            UnknownObjects = true,
            CinematicClipKey = clip.Key,
            CinematicDocument = document
        });
        using var exported = new GlbReader(exportPath);
        int nodeIndex = Enumerable.Range(0, exported.Root.GetProperty("nodes").GetArrayLength())
            .Single(index => exported.Root.GetProperty("nodes")[index].GetProperty("name").GetString()
                == $"object {targetKey:X8}");
        bool rotationChannelExported = exported.Root.GetProperty("animations")[0].GetProperty("channels")
            .EnumerateArray().Any(channel => channel.GetProperty("target").GetProperty("node").GetInt32() == nodeIndex
                && channel.GetProperty("target").GetProperty("path").GetString() == "rotation");
        Matrix4x4 exportedPose = exported.World(nodeIndex, 0, eventStart + eventDuration);
        Matrix4x4 expectedRenderPose = AnimationMath.ToRender(
            CinematicLocalRotationTimeline.RotateLocalYAxis(Matrix4x4.Identity, angle));
        Matrix4x4.Decompose(expectedRenderPose, out _, out Quaternion expectedRotation, out _);
        Matrix4x4.Decompose(exportedPose, out _, out Quaternion exportedRotation, out _);
        float rotationAgreement = MathF.Abs(Quaternion.Dot(expectedRotation, exportedRotation));
        if (!rotationChannelExported || rotationAgreement < .999f)
            throw new InvalidDataException($"GLB export omitted or changed a nested OBJRotateLocalY animation. "
                + $"rotationChannel={rotationChannelExported}; agreement={rotationAgreement:F4}.");
        playback.Deactivate();
    }

    private static void VerifyCallbackBoundSceneEvents(string outputDirectory)
    {
        var world = World(0x10, 0x100, 0x200);
        var clip = new AnimationClip
        {
            Key = 0x150, Name = "callback-bound", Duration = 5,
            CinematicWorldKey = 0x10, Cinematic = true, SceneMotion = true,
            Tracks = [new AnimationTrack
            {
                Bone = -1,
                Target = 0,
                Keys = [CinematicTestEvents.Call(2, 0x100, 0x2917, 2166)]
            }]
        };
        world.CinematicClips.Add(clip.Key, clip);
        var owner = new CinematicScriptOwner(0x100, 0, 0, 0x10, uint.MaxValue, clip.Key,
            "callback owner", ["Cine_Events_Cine_exec_GoToEvents"], [], [])
        {
            ModelFunctionNames = ["Cine_Events_Cine_exec_GoToEvents"]
        };
        var document = new SceneDocument();
        document.Worlds.Add(world);
        document.CinematicClips.Add(clip.Key, clip);
        document.CinematicScriptOwners[owner.Id] = owner;
        AddList(document, List(owner.OwnerKey, world.Key, clip.Key,
        [
            TransformChannel(0, 0x200, 0, 3) with
            {
                ChannelFlags = CinematicEventTrackFlags.RunningInit,
                Calls =
                [
                    (.5f, new CinematicLiteralCall(0x200, 8152, [1])),
                    (.75f, new CinematicLiteralCall(0x100, 0x2916, [2166]))
                ]
            }
        ]));

        bool sourceRouteExists = CinematicScriptReader.ReachableEngineCalls(owner)
            .Any(call => call.Api == "TRAPlayAll");
        var timeline = CinematicNestedEventTransformTracks.ReadTimeline(clip, world, document);
        var disabledTimeline = CinematicNestedEventTransformTracks.ReadTimeline(clip, world, document,
            new HashSet<CinematicScriptOwnerId> { owner.Id });
        AnimationTrack[] tracks = timeline.TransformTracks;
        var state = new CinematicObjectState(clip, world, [], timeline.Commands);
        state.Evaluate(.49f);
        bool remainsVisibleBeforeEvent = !world.Objects.Single(sceneObject => sceneObject.GaoKey == 0x200).IsControlHidden;
        state.Evaluate(.5f);
        bool hiddenAtEvent = world.Objects.Single(sceneObject => sceneObject.GaoKey == 0x200).IsControlHidden;
        if (!sourceRouteExists || tracks.Length != 1 || tracks[0].Target != 0x200
            || tracks[0].Keys[0].Time != 0)
            throw new InvalidDataException("A source-reachable TRAPlayAll callback on the selected clip's own SceneEvents list must seed EVE transform playback at clip time zero.");
        if (!remainsVisibleBeforeEvent || !hiddenAtEvent)
            throw new InvalidDataException("Callback-bound SceneEvents commands must update object visibility on the shared cinematic clock.");

        var enabledPlayback = CinematicPlayback.Discover(document, world, new(), null).Single();
        bool directWriteRetained = enabledPlayback.ScriptCalls.Any(item => item.Time == 2
            && item.Call.Selector == 0x2917 && item.Call.Arguments.SequenceEqual([2166u]));
        bool nestedWriteIncluded = enabledPlayback.ScriptStateCalls.Any(item => item.Time == .75f
            && item.Call.Selector == 0x2916 && item.Call.Arguments.SequenceEqual([2166u]));
        if (!directWriteRetained || !nestedWriteIncluded)
            throw new InvalidDataException("The playhead flag state must combine direct clip writes with callback-resolved SceneEvents writes.");
        enabledPlayback.Deactivate();
        // Keep the standalone script-state assertion out of the GLB export fixture:
        // the export checks below should measure only callback-resolved EVE motion.
        clip.Tracks = [];

        if (!timeline.CallbackSceneEventOwners.SequenceEqual([owner.Id])
            || !disabledTimeline.CallbackSceneEventOwners.SequenceEqual([owner.Id])
            || timeline.PreviewableSceneEventOwners.Length != 0
            || disabledTimeline.TransformTracks.Length != 0 || disabledTimeline.Commands.Length != 0)
            throw new InvalidDataException("Disabling one callback owner must suppress its SceneEvents overlays while retaining source provenance for the UI.");

        var disabledPlayback = CinematicPlayback.Discover(document, world, new(), null,
            new HashSet<CinematicScriptOwnerId> { owner.Id }).Single();
        disabledPlayback.Activate();
        disabledPlayback.Seek(1);
        if (disabledPlayback.CallbackSceneEventOwners.Length != 1
            || world.Objects.Single(sceneObject => sceneObject.GaoKey == 0x200).PreviewMatrix != null)
            throw new InvalidDataException("A clip must remain inspectable after disabling its only callback-started EVE track.");
        disabledPlayback.Deactivate();

        string enabledExportPath = Path.Combine(outputDirectory, "cinematic-callback-owner-enabled.glb");
        SceneExporter.Export(enabledExportPath, world, new(), new()
        {
            Animations = true,
            UnknownObjects = true,
            GameHidden = true,
            CinematicClipKey = clip.Key,
            CinematicDocument = document
        });
        using var enabledExport = new GlbReader(enabledExportPath);
        bool callbackMotionExported = enabledExport.Root.GetProperty("animations").GetArrayLength() == 1;
        string disabledExportPath = Path.Combine(outputDirectory, "cinematic-callback-owner-disabled.glb");
        SceneExporter.Export(disabledExportPath, world, new(), new()
        {
            Animations = true,
            UnknownObjects = true,
            GameHidden = true,
            CinematicClipKey = clip.Key,
            CinematicDocument = document,
            DisabledCinematicCallbackOwners = new HashSet<CinematicScriptOwnerId> { owner.Id }
        });
        using var disabledExport = new GlbReader(disabledExportPath);
        bool disabledCallbackMotionOmitted = !disabledExport.Root.TryGetProperty("animations", out var disabledAnimations)
            || disabledAnimations.GetArrayLength() == 0;
        if (!callbackMotionExported || !disabledCallbackMotionOmitted)
            throw new InvalidDataException("Cinematic export must include or omit callback-linked motion according to the selected script-owner switches.");
    }

    private static void VerifyRunningInitHonorsOwnerSelection()
    {
        var world = World(0x10, 0x100, 0x200);
        var clip = Clip(0x100, 0, 0x3B64, 0);
        var owner = new CinematicScriptOwner(0x200, 0, 0, world.Key,
            uint.MaxValue, 0x250, "manual EVE owner", [], [], []);
        var document = new SceneDocument();
        document.Worlds.Add(world);
        document.CinematicClips[clip.Key] = clip;
        document.CinematicScriptOwners[owner.Id] = owner;
        AddList(document, List(owner.OwnerKey, world.Key, owner.SceneEventsKey,
        [
            TransformChannel(0, owner.OwnerKey, 0, 8) with
            {
                ChannelFlags = CinematicEventTrackFlags.RunningInit
            }
        ]));

        var unselected = CinematicNestedEventTransformTracks.ReadTimeline(clip, world, document);
        var selected = CinematicNestedEventTransformTracks.ReadTimeline(clip, world, document,
            previewedSceneEventOwners: new HashSet<CinematicScriptOwnerId> { owner.Id });
        if (!unselected.PreviewableSceneEventOwners.SequenceEqual([owner.Id])
            || unselected.TransformTracks.Length != 0
            || selected.TransformTracks.Length != 1
            || selected.TransformTracks[0].Target != owner.OwnerKey)
            throw new InvalidDataException("A script-owned RunningInit channel must obey its owner preview switch instead of bypassing it during default scheduling.");
    }

    private static void VerifyNestedLifecycleUsesCinematicClock()
    {
        var world = World(0x10, 0x100, 0x200, 0x300);
        var document = new SceneDocument();
        var parentChannel = new CinematicEmbeddedEventTrack(0, 0x100, -1, 0, 2, 3)
        {
            Calls =
            [
                (.5f, new CinematicLiteralCall(0x200, 0x3B64, [0])), // TRAPlay child track
                (1f, new CinematicLiteralCall(0x200, 0x3B65, [0]))   // TRAStop child track
            ]
        };
        var childChannel = new CinematicEmbeddedEventTrack(0, 0x300, -1, 0, 2, 1)
        {
            SourceTrack = new AnimationTrack
            {
                Bone = -1,
                Keys =
                [
                    new AnimationKey { Time = 0, Duration = 1, Position = Vector3.Zero },
                    new AnimationKey { Time = 1, Position = new Vector3(5, 0, 0) }
                ]
            }
        };
        AddList(document, List(0x100, 0x10, 0x150, [parentChannel]));
        AddList(document, List(0x200, 0x10, 0x250, [childChannel]));

        AnimationTrack[] stoppedDuringPlayback = Read(world, document, Clip(0x100, 2, 0x3B60));
        if (stoppedDuringPlayback.Length != 1 || stoppedDuringPlayback[0].CinematicPlaybackClock == null)
            throw new InvalidDataException("A child EVE stop must produce a clocked transform segment on the parent's absolute cinematic clock.");
        var beforeStop = CinematicTransformSampler.Evaluate(new(), stoppedDuringPlayback, 2.9f);
        var atStop = CinematicTransformSampler.Evaluate(new(), stoppedDuringPlayback, 3);
        var heldAfterStop = CinematicTransformSampler.Evaluate(new(), stoppedDuringPlayback, 3.5f);
        if (MathF.Abs(beforeStop.M41 - 2) > .02f || MathF.Abs(atStop.M41 - 2.5f) > .02f
            || MathF.Abs(heldAfterStop.M41 - atStop.M41) > .001f)
            throw new InvalidDataException("A nested EVE stop must freeze the interpolated transform at the command time.");

        parentChannel = parentChannel with
        {
            Calls =
            [
                (.5f, new CinematicLiteralCall(0x200, 0x3B64, [0])), // TRAPlay child track
                (2f, new CinematicLiteralCall(0x200, 0x3B65, [0]))   // TRAStop after its duration
            ]
        };
        AddList(document, List(0x100, 0x10, 0x150, [parentChannel]));
        AnimationTrack[] stoppedAfterCompletion = Read(world, document, Clip(0x100, 2, 0x3B60));
        var afterCompletionPose = CinematicTransformSampler.Evaluate(new(), stoppedAfterCompletion, 4.5f);
        if (stoppedAfterCompletion.Length != 1
            || MathF.Abs(afterCompletionPose.M41 - 5) > .001f)
            throw new InvalidDataException("A child EVE stop after completion must preserve the transform at its shifted cinematic start.");

        parentChannel = parentChannel with
        {
            Calls =
            [
                (.5f, new CinematicLiteralCall(0x200, 0x3B64, [0])), // TRAPlay child track
                (1f, new CinematicLiteralCall(0x200, 0x3B66, [0])), // TRAPause child track
                (2f, new CinematicLiteralCall(0x200, 0x3B67, [0]))  // TRAUnPause child track
            ]
        };
        AddList(document, List(0x100, 0x10, 0x150, [parentChannel]));
        AnimationTrack[] pausedDuringPlayback = Read(world, document, Clip(0x100, 2, 0x3B60));
        var atPause = CinematicTransformSampler.Evaluate(new(), pausedDuringPlayback, 3);
        var pausedPose = CinematicTransformSampler.Evaluate(new(), pausedDuringPlayback, 3.5f);
        var resumedPose = CinematicTransformSampler.Evaluate(new(), pausedDuringPlayback, 4.5f);
        if (pausedDuringPlayback.Length != 1 || pausedDuringPlayback[0].CinematicPlaybackClock == null
            || MathF.Abs(atPause.M41 - 2.5f) > .02f
            || MathF.Abs(pausedPose.M41 - atPause.M41) > .001f
            || resumedPose.M41 <= pausedPose.M41)
            throw new InvalidDataException("A paused nested EVE transform must freeze and resume its source-local clock.");

        var actorRoot = new SceneObject { GaoKey = 0x300, Name = "clocked actor root" };
        var actor = new SceneActor { Root = actorRoot, Members = [actorRoot], Bones = [actorRoot] };
        actor.Clip = new AnimationClip
        {
            Cinematic = true,
            Loaded = true,
            Duration = 5,
            Tracks = [stoppedDuringPlayback[0]]
        };
        actor.Time = 3.5f;
        actor.Evaluate(0);
        if (MathF.Abs(actorRoot.PreviewMatrix!.Value.M41 - 2.5f) > .001f)
            throw new InvalidDataException("Cinematic actor roots must use the same nested EVE stop clock as scene objects.");
    }

    private static void VerifyNestedTrackFlags(string outputDirectory)
    {
        var initialWorld = World(0x10, 0x100, 0x200);
        var initialDocument = new SceneDocument();
        AddList(initialDocument, List(0x100, 0x10, 0x150,
        [
            TransformChannel(0, 0x200, 0, 7) with
            {
                ChannelFlags = CinematicEventTrackFlags.RunningInit | CinematicEventTrackFlags.AutoLoop,
                Calls = [(.25f, new CinematicLiteralCall(0x200, 8115, [0, 0x00600000]))]
            }
        ]));
        var initialClip = new AnimationClip
        {
            Key = 0x500,
            Duration = 3,
            CinematicWorldKey = initialWorld.Key
        };
        var initialTimeline = CinematicNestedEventTransformTracks.ReadTimeline(
            initialClip, initialWorld, initialDocument);
        bool runningInitTrackStartsAtClipStart = initialTimeline.TransformTracks.Length == 1
            && initialTimeline.TransformTracks[0].Keys[0].Time == 0
            && initialTimeline.TransformTracks[0].CinematicPlaybackClock != null;
        bool runningInitCommandsUseSameClock = initialTimeline.Commands.Length == 3
            && initialTimeline.Commands.Select(item => item.Time)
                .Zip([.25f, 1.25f, 2.25f])
                .All(pair => MathF.Abs(pair.First - pair.Second) < .001f);

        var loopingClock = new CinematicTrackPlaybackClock(0, 1, 2.5f,
            CinematicEventTrackFlags.AutoLoop, []);
        bool mapsFirstLoop = loopingClock.TryMap(.75f, out float firstLoopTime)
            && MathF.Abs(firstLoopTime - .75f) < .001f;
        bool mapsNextLoop = loopingClock.TryMap(1.25f, out float nextLoopTime)
            && MathF.Abs(nextLoopTime - .25f) < .001f;
        float[] repeatedKeys = loopingClock.TimelineTimesForLocal(.5f).ToArray();
        var longRunningLoopClock = new CinematicTrackPlaybackClock(0, 2.6f, 44.78f,
            CinematicEventTrackFlags.AutoLoop, []);
        bool longLoopMapsWithoutPrecisionStall = longRunningLoopClock.TryMap(44.78f, out float longLoopTime)
            && MathF.Abs(longLoopTime - (44.78f % 2.6f)) < .001f
            && longRunningLoopClock.TimelineTimesForLocal(.5f).Count() > 10;
        var unrepresentableLoopClock = new CinematicTrackPlaybackClock(0, 1e-8f, 44.78f,
            CinematicEventTrackFlags.AutoLoop, []);
        bool unrepresentableLoopTerminates = unrepresentableLoopClock.TryMap(44.78f, out float tinyLoopTime)
            && tinyLoopTime == 0;

        var persistentClock = new CinematicTrackPlaybackClock(0, 1, 3,
            0,
        [
            (1.5f, "TRAPlay"),
            (2, "TRAStop"),
            (2.5f, "TRAPlay")
        ]);
        bool repeatWhileRunningIsNoOp = persistentClock.TryMap(1.75f, out float heldTime)
            && MathF.Abs(heldTime - 1) < .001f;
        bool stoppedTrackRestarts = persistentClock.TryMap(2.75f, out float restartedTime)
            && MathF.Abs(restartedTime - .25f) < .001f;

        var autoStopClock = new CinematicTrackPlaybackClock(0, 1, 2,
            CinematicEventTrackFlags.AutoStop, [(1.5f, "TRAPlay")]);
        bool autoStopPlayRestarts = autoStopClock.TryMap(1.75f, out float autoStopRestartTime)
            && MathF.Abs(autoStopRestartTime - .25f) < .001f;

        var setEndClock = new CinematicTrackPlaybackClock(0, 2, 3,
            CinematicEventTrackFlags.AutoStop, [(1, "TRASetEnd")]);
        bool setEndJumpsToLastFrame = setEndClock.TryMap(.99f, out float timeBeforeSetEnd)
            && MathF.Abs(timeBeforeSetEnd - .99f) < .001f
            && setEndClock.TryMap(1, out float timeAtSetEnd) && timeAtSetEnd == 2
            && setEndClock.TryMap(2.5f, out float timeAfterSetEnd) && timeAfterSetEnd == 2;

        var stoppedReinitClock = new CinematicTrackPlaybackClock(0, 2, 4,
            CinematicEventTrackFlags.AutoStop,
            [(1, "TRAReinit"), (2.5f, "TRAPlay")]);
        bool reinitStopsNonRunningInitTrack = stoppedReinitClock.TryMap(.5f, out float beforeReinit)
            && MathF.Abs(beforeReinit - .5f) < .001f
            && stoppedReinitClock.TryMap(1, out float atReinit) && atReinit == 0
            && stoppedReinitClock.TryMap(2, out float whileStopped) && whileStopped == 0
            && stoppedReinitClock.TryMap(2.75f, out float replayedAfterReinit)
            && MathF.Abs(replayedAfterReinit - .25f) < .001f;
        var runningInitReinitClock = new CinematicTrackPlaybackClock(0, 2, 3,
            CinematicEventTrackFlags.RunningInit, [(1, "TRAReinitAll")]);
        bool reinitRestartsRunningInitTrack = runningInitReinitClock.TryMap(.75f, out float beforeReinitAll)
            && MathF.Abs(beforeReinitAll - .75f) < .001f
            && runningInitReinitClock.TryMap(1, out float atReinitAll) && atReinitAll == 0
            && runningInitReinitClock.TryMap(1.5f, out float afterReinitAll)
            && MathF.Abs(afterReinitAll - .5f) < .001f;

        var setTimeClock = CinematicTrackPlaybackClock.FromCommands(0, 10, 5,
            CinematicEventTrackFlags.RunningInit,
            [new(.5f, "TRASetTime", 4), new(2, "TRAStop"), new(3, "TRASetTime", 7)], 8);
        bool setTimeSeeksRunningTrack = setTimeClock.TryMap(.75f, out float afterRunningSeek)
            && MathF.Abs(afterRunningSeek - 4.25f) < .001f;
        bool setTimePreservesStoppedTrack = setTimeClock.TryMap(2.5f, out float stoppedCursor)
            && MathF.Abs(stoppedCursor - 5.5f) < .001f
            && setTimeClock.TryMap(3.5f, out float seekWhileStopped) && seekWhileStopped == 7;
        var pausedSetTimeClock = CinematicTrackPlaybackClock.FromCommands(0, 10, 5,
            CinematicEventTrackFlags.RunningInit,
            [new(1, "TRAPause"), new(2, "TRASetTime", 3), new(4, "TRAUnPause")], 8);
        bool setTimePreservesPausedTrack = pausedSetTimeClock.TryMap(3.5f, out float pausedCursor)
            && pausedCursor == 3
            && pausedSetTimeClock.TryMap(4.5f, out float resumedCursor)
            && MathF.Abs(resumedCursor - 3.5f) < .001f;
        var pastEndSetTimeClock = CinematicTrackPlaybackClock.FromCommands(0, 10, 2,
            CinematicEventTrackFlags.RunningInit, [new(1, "TRASetTime", 12)], 8);
        bool setTimeClampsPastEndToLastEvent = pastEndSetTimeClock.TryMap(1, out float lastEventStart)
            && lastEventStart == 8;
        var invalidSetTimeClock = CinematicTrackPlaybackClock.FromCommands(0, 10, 2,
            CinematicEventTrackFlags.RunningInit, [new(1, "TRASetTime", float.NaN)], 8);
        bool invalidSetTimeIsIgnored = invalidSetTimeClock.TryMap(1.5f, out float timeAfterInvalidSeek)
            && MathF.Abs(timeAfterInvalidSeek - 1.5f) < .001f;
        var wrappedSetTimeClock = CinematicTrackPlaybackClock.FromCommands(0, 2000, 2,
            CinematicEventTrackFlags.RunningInit, [new(1, "TRASetTime", 1100)], 1999);
        bool wrappedSetTimeIsNotGuessed = wrappedSetTimeClock.TryMap(1.5f, out float timeAfterWrappedSeek)
            && MathF.Abs(timeAfterWrappedSeek - 1.5f) < .001f;

        var setTimeWorld = World(0x10, 0x100, 0x200, 0x300);
        var setTimeDocument = new SceneDocument();
        AddList(setTimeDocument, List(0x100, 0x10, 0x150,
        [
            new CinematicEmbeddedEventTrack(0, 0x200, -1, 0, 2, 2)
            {
                ChannelFlags = CinematicEventTrackFlags.RunningInit,
                SourceTrack = new AnimationTrack { Bone = -1, Keys =
                    [new() { Time = 0, Duration = 1, Position = Vector3.Zero },
                     new() { Time = 1, Duration = 1, Position = Vector3.One }] }
            },
            new CinematicEmbeddedEventTrack(1, 0x300, -1, 0, 2, 2)
            {
                ChannelFlags = CinematicEventTrackFlags.RunningInit,
                SourceTrack = new AnimationTrack { Bone = -1, Keys =
                    [new() { Time = 0, Duration = 1, Position = Vector3.Zero },
                     new() { Time = 1, Duration = 1, Position = Vector3.One }] }
            }
        ]));
        var setTimeClip = Clip(0x100, 1, 0x3B6D, BitConverter.SingleToUInt32Bits(.5f));
        AnimationTrack[] setTimeTracks = Read(setTimeWorld, setTimeDocument, setTimeClip);
        bool setTimeSelectsAllChannels = setTimeTracks.Length == 2
            && setTimeTracks.All(track => track.CinematicPlaybackClock != null
                && track.CinematicPlaybackClock.TryMap(1.25f, out float localTime)
                && MathF.Abs(localTime - .75f) < .001f);
        setTimeClip.Cinematic = true;
        setTimeClip.SceneMotion = true;
        setTimeWorld.CinematicClips.Add(setTimeClip.Key, setTimeClip);
        setTimeDocument.Worlds.Add(setTimeWorld);
        setTimeDocument.CinematicClips.Add(setTimeClip.Key, setTimeClip);
        var setTimePlayback = CinematicPlayback.Discover(setTimeDocument, setTimeWorld, new()).Single();
        setTimePlayback.Activate();
        setTimePlayback.Seek(1.25f);
        bool setTimePreviewMatchesBothChannels = setTimeWorld.Objects
            .Where(sceneObject => sceneObject.GaoKey is 0x200 or 0x300)
            .All(sceneObject => MathF.Abs(sceneObject.PreviewMatrix!.Value.M41 - .75f) < .02f);
        string setTimeExportPath = Path.Combine(outputDirectory, "cinematic-nested-set-time.glb");
        SceneExporter.Export(setTimeExportPath, setTimeWorld, new(), new()
        {
            Animations = true,
            UnknownObjects = true,
            CinematicClipKey = setTimeClip.Key,
            CinematicDocument = setTimeDocument
        });
        using var setTimeExport = new GlbReader(setTimeExportPath);
        bool setTimeExportMatchesBothChannels = setTimeWorld.Objects
            .Where(sceneObject => sceneObject.GaoKey is 0x200 or 0x300)
            .All(sceneObject =>
            {
                int node = Enumerable.Range(0, setTimeExport.Root.GetProperty("nodes").GetArrayLength())
                    .Single(index => setTimeExport.Root.GetProperty("nodes")[index].GetProperty("name")
                        .GetString() == sceneObject.Name);
                return MathF.Abs(setTimeExport.World(node, 0, 1.25f).M41 - .75f) < .02f;
            });

        var setEndWorld = World(0x10, 0x100, 0x200);
        var setEndDocument = new SceneDocument();
        AddList(setEndDocument, List(0x100, 0x10, 0x150,
        [new CinematicEmbeddedEventTrack(0, 0x200, -1, 0, 2, 2)
        {
            ChannelFlags = CinematicEventTrackFlags.RunningInit,
            SourceTrack = new AnimationTrack
            {
                Bone = -1,
                Keys =
                [
                    new AnimationKey { Time = 0, Duration = 2, Position = Vector3.Zero },
                    new AnimationKey { Time = 2, Position = new Vector3(2, 0, 0) }
                ]
            }
        }]));
        AnimationTrack[] setEndTransform = Read(setEndWorld, setEndDocument,
            Clip(0x100, 1, 0x3B6C, 0));
        Matrix4x4 beforeSetEndPose = CinematicTransformSampler.Evaluate(new(), setEndTransform, .99f);
        Matrix4x4 atSetEndPose = CinematicTransformSampler.Evaluate(new(), setEndTransform, 1);
        Matrix4x4 heldSetEndPose = CinematicTransformSampler.Evaluate(new(), setEndTransform, 2);
        bool setEndTransformsUseSourceCall = setEndTransform.Length == 1
            && setEndTransform[0].CinematicPlaybackClock != null
            && MathF.Abs(beforeSetEndPose.M41 - .99f) < .02f
            && MathF.Abs(atSetEndPose.M41 - 2) < .001f
            && MathF.Abs(heldSetEndPose.M41 - 2) < .001f;

        var reinitWorld = World(0x10, 0x100, 0x200);
        var reinitDocument = new SceneDocument();
        AddList(reinitDocument, List(0x100, 0x10, 0x150,
        [new CinematicEmbeddedEventTrack(0, 0x200, -1, 0, 2, 2)
        {
            ChannelFlags = CinematicEventTrackFlags.RunningInit,
            SourceTrack = new AnimationTrack
            {
                Bone = -1,
                Keys =
                [
                    new AnimationKey { Time = 0, Duration = 2, Position = Vector3.Zero },
                    new AnimationKey { Time = 2, Position = new Vector3(2, 0, 0) }
                ]
            }
        }]));
        AnimationTrack[] reinitAllTransform = Read(reinitWorld, reinitDocument,
            Clip(0x100, 1, 0x3B68));
        Matrix4x4 atReinitAllPose = CinematicTransformSampler.Evaluate(new(), reinitAllTransform, 1);
        Matrix4x4 afterReinitAllPose = CinematicTransformSampler.Evaluate(new(), reinitAllTransform, 1.5f);
        bool reinitAllTransformsUseSourceCall = reinitAllTransform.Length == 1
            && reinitAllTransform[0].CinematicPlaybackClock != null
            && MathF.Abs(atReinitAllPose.M41) < .001f
            && MathF.Abs(afterReinitAllPose.M41 - .5f) < .001f;

        var reinitClip = Clip(0x100, 1, 0x3B68);
        reinitClip.Cinematic = true;
        reinitClip.SceneMotion = true;
        reinitWorld.CinematicClips.Add(reinitClip.Key, reinitClip);
        reinitDocument.Worlds.Add(reinitWorld);
        reinitDocument.CinematicClips.Add(reinitClip.Key, reinitClip);
        var reinitPlayback = CinematicPlayback.Discover(reinitDocument, reinitWorld, new()).Single();
        reinitPlayback.Activate();
        reinitPlayback.Seek(1.5f);
        SceneObject reinitTarget = reinitWorld.Objects.Single(item => item.GaoKey == 0x200);
        bool reinitPlaybackUsesResetClock = MathF.Abs(reinitTarget.PreviewMatrix!.Value.M41 - .5f) < .001f;

        string reinitExportPath = Path.Combine(outputDirectory, "cinematic-nested-reinit-all.glb");
        SceneExporter.Export(reinitExportPath, reinitWorld, new(), new()
        {
            Animations = true,
            UnknownObjects = true,
            CinematicClipKey = reinitClip.Key,
            CinematicDocument = reinitDocument
        });
        using var reinitExport = new GlbReader(reinitExportPath);
        int reinitNode = Enumerable.Range(0, reinitExport.Root.GetProperty("nodes").GetArrayLength())
            .Single(index => reinitExport.Root.GetProperty("nodes")[index].GetProperty("name").GetString()
                == reinitTarget.Name);
        Matrix4x4 reinitExportPose = reinitExport.World(reinitNode, 0, 1.5f);
        bool reinitExportUsesResetClock = MathF.Abs(reinitExportPose.M41 - .5f) < .001f;

        if (!runningInitTrackStartsAtClipStart || !runningInitCommandsUseSameClock
            || !mapsFirstLoop || !mapsNextLoop || repeatedKeys.Length != 3
            || !longLoopMapsWithoutPrecisionStall || !unrepresentableLoopTerminates || !setEndJumpsToLastFrame
            || !setEndTransformsUseSourceCall || !reinitStopsNonRunningInitTrack
            || !reinitRestartsRunningInitTrack || !reinitAllTransformsUseSourceCall
            || !reinitPlaybackUsesResetClock || !reinitExportUsesResetClock
            || !repeatWhileRunningIsNoOp || !stoppedTrackRestarts || !autoStopPlayRestarts
            || !setTimeSeeksRunningTrack || !setTimePreservesStoppedTrack || !setTimePreservesPausedTrack
            || !setTimeClampsPastEndToLastEvent || !invalidSetTimeIsIgnored || !setTimeSelectsAllChannels
            || !setTimePreviewMatchesBothChannels || !setTimeExportMatchesBothChannels || !wrappedSetTimeIsNotGuessed)
            throw new InvalidDataException($"Nested SceneEvents must seed RunningInit tracks and honor AutoLoop, AutoStop, TRASetEnd, TRASetTime, TRAReinit, TRAReinitAll and play-while-running source semantics in preview and GLB export. setEndClock={setEndJumpsToLastFrame}, setEndTransform={setEndTransformsUseSourceCall}, reinitStops={reinitStopsNonRunningInitTrack}, reinitRestarts={reinitRestartsRunningInitTrack}, reinitTransform={reinitAllTransformsUseSourceCall}, reinitPreview={reinitPlaybackUsesResetClock}, reinitExport={reinitExportUsesResetClock}, setTimeRunning={setTimeSeeksRunningTrack}, setTimeStopped={setTimePreservesStoppedTrack}, setTimePaused={setTimePreservesPausedTrack}, setTimeEnd={setTimeClampsPastEndToLastEvent}, setTimeInvalid={invalidSetTimeIsIgnored}, setTimeWrap={wrappedSetTimeIsNotGuessed}, setTimeAll={setTimeSelectsAllChannels}, setTimePreview={setTimePreviewMatchesBothChannels}, setTimeExport={setTimeExportMatchesBothChannels}.");
    }

    private static void VerifyStopAndAmbiguousRoutesAreOmitted()
    {
        var world = World(0x10, 0x100, 0x200);
        var document = new SceneDocument();
        var finiteTrack = new CinematicEmbeddedEventTrack(0, 0x200, -1, 0, 2, 1)
        {
            ChannelFlags = CinematicEventTrackFlags.AutoStop,
            SourceTrack = new AnimationTrack
            {
                Bone = -1,
                Keys =
                [
                    new AnimationKey { Time = 0, Duration = 1, Position = Vector3.Zero },
                    new AnimationKey { Time = 1, Position = new Vector3(3, 0, 0) }
                ]
            }
        };
        AddList(document, List(0x100, 0x10, 0x150, [finiteTrack]));

        var stopAfterCompletion = Clip(0x100, 0, 0x3B60);
        stopAfterCompletion.Tracks[0].Keys.Add(CinematicTestEvents.Call(2, 0x100, 0x3B61)); // TRAStopAll
        if (Read(world, document, stopAfterCompletion).Length != 1)
            throw new InvalidDataException("A stop after every selected channel's authored duration must preserve its completed transform.");

        var stopDuringPlayback = Clip(0x100, 0, 0x3B60);
        stopDuringPlayback.Tracks[0].Keys.Add(CinematicTestEvents.Call(.5f, 0x100, 0x3B61)); // TRAStopAll
        AnimationTrack[] stopped = Read(world, document, stopDuringPlayback);
        if (stopped.Length != 1 || stopped[0].CinematicPlaybackClock == null
            || MathF.Abs(CinematicTransformSampler.Evaluate(new(), stopped, .5f).M41 - 1.5f) > .02f)
            throw new InvalidDataException("A mid-track stop must keep the last interpolated nested EVE transform.");

        var playWhileRunning = Clip(0x100, 0, 0x3B60);
        playWhileRunning.Tracks[0].Keys.Add(CinematicTestEvents.Call(.5f, 0x100, 0x3B64, 0)); // TRAPlay
        AnimationTrack[] unchangedPlayback = Read(world, document, playWhileRunning);
        if (unchangedPlayback.Length != 1 || unchangedPlayback[0].Keys[0].Time != 0)
            throw new InvalidDataException("TRAPlay on a running channel must remain a no-op instead of suppressing its original transform.");

        var replayAfterCompletion = Clip(0x100, 0, 0x3B60);
        replayAfterCompletion.Tracks[0].Keys.Add(CinematicTestEvents.Call(2, 0x100, 0x3B64, 0)); // TRAPlay
        AnimationTrack[] replayed = Read(world, document, replayAfterCompletion);
        var replayPose = CinematicTransformSampler.Evaluate(new(), replayed, 2);
        if (replayed.Length != 1 || replayed[0].CinematicPlaybackClock == null
            || MathF.Abs(replayPose.M41 - 0) > .001f)
            throw new InvalidDataException("A later TRAPlay must reset a completed channel's source-local clock to zero.");

        AddList(document, List(0x100, 0x11, 0x150, [TransformChannel(0, 0x200, 0, 3)]));
        var ambiguousWorld = World(0x12, 0x100, 0x200);
        if (Read(ambiguousWorld, document, Clip(0x100, 1, 0x3B60)).Length != 0)
            throw new InvalidDataException("A nested event list from multiple nonmatching worlds must remain unresolved.");
    }

    private static void VerifyManualSceneEventOwnerPreview(string outputDirectory)
    {
        var cinematicWorld = World(0x10, 0x100, 0x200);
        cinematicWorld.Name = "sample_cine";
        var environmentWorld = World(0x11, 0x300, 0x400);
        environmentWorld.Name = "sample";
        var assembledWorld = World(0x10, 0x100, 0x200, 0x300, 0x400);
        assembledWorld.Name = "sample_cine · assembled";
        assembledWorld.Objects.Single(item => item.GaoKey == 0x400).ControlFlags = CinematicObjectState.Invisible;
        var clip = Clip(0x100, 0, 0x3B60);
        clip.Key = 0x500;
        clip.CinematicWorldKey = cinematicWorld.Key;
        clip.Cinematic = true;
        clip.SceneMotion = true;
        assembledWorld.CinematicClips.Add(clip.Key, clip);

        var owner = new CinematicScriptOwner(0x300, 0, 0, environmentWorld.Key,
            uint.MaxValue, 0x301, "paired-world script", ["Cine_Events_cine_firstinit"], [], []);
        var document = new SceneDocument();
        document.Worlds.Add(cinematicWorld);
        document.Worlds.Add(environmentWorld);
        document.CinematicClips[clip.Key] = clip;
        document.CinematicScriptOwners[owner.Id] = owner;
        AddList(document, List(owner.OwnerKey, owner.WorldKey, owner.SceneEventsKey,
        [
            TransformChannel(0, 0x400, 0, 8) with
            {
                Calls = [(.5f, new CinematicLiteralCall(0x400, 8115, [0, 0x00600000]))]
            }
        ]));

        var unselected = CinematicNestedEventTransformTracks.ReadTimeline(clip, assembledWorld, document);
        var selected = CinematicNestedEventTransformTracks.ReadTimeline(clip, assembledWorld, document,
            previewedSceneEventOwners: new HashSet<CinematicScriptOwnerId> { owner.Id });
        var state = new CinematicObjectState(clip, assembledWorld, [], selected.Commands);
        state.Evaluate(.49f);
        bool commandWaitsForItsSourceTime = assembledWorld.Objects.Single(item => item.GaoKey == 0x400).IsControlHidden;
        state.Evaluate(.5f);
        bool commandAppliesAtSourceTime = !assembledWorld.Objects.Single(item => item.GaoKey == 0x400).IsControlHidden;

        if (!unselected.PreviewableSceneEventOwners.SequenceEqual([owner.Id])
            || unselected.TransformTracks.Length != 0 || unselected.Commands.Length != 0
            || !selected.PreviewableSceneEventOwners.SequenceEqual([owner.Id])
            || selected.TransformTracks.Length != 1 || selected.TransformTracks[0].Target != 0x400
            || selected.TransformTracks[0].Keys[0].Position?.X != 8
            || !commandWaitsForItsSourceTime || !commandAppliesAtSourceTime)
            throw new InvalidDataException($"A paired-world SceneEvents preview failed: candidates={unselected.PreviewableSceneEventOwners.Length}, "
                + $"defaultTracks={unselected.TransformTracks.Length}, defaultCalls={unselected.Commands.Length}, "
                + $"selectedTracks={selected.TransformTracks.Length}, selectedCalls={selected.Commands.Length}, "
                + $"before={commandWaitsForItsSourceTime}, after={commandAppliesAtSourceTime}.");

        string exportPath = Path.Combine(outputDirectory, "cinematic-manual-script-owner-preview.glb");
        SceneExporter.Export(exportPath, assembledWorld, new(), new()
        {
            Animations = true,
            UnknownObjects = true,
            GameHidden = true,
            CinematicClipKey = clip.Key,
            CinematicDocument = document,
            PreviewedCinematicSceneEventOwners = new HashSet<CinematicScriptOwnerId> { owner.Id }
        });
        using var exported = new GlbReader(exportPath);
        if (exported.Root.GetProperty("animations").GetArrayLength() != 1)
            throw new InvalidDataException("Manually previewed source-owned EVE transforms must be included in synchronized scene export.");
    }

    private static void VerifyPlaybackAndExport(string outputDirectory)
    {
        var world = World(0x10, 0x100, 0x200);
        var target = world.Objects.Single(sceneObject => sceneObject.GaoKey == 0x200);
        target.Transform.Translation = new Vector3(9, 0, 0);
        target.ControlFlags = CinematicObjectState.Invisible | CinematicObjectState.Inactive;
        var clip = Clip(0x100, 1, 0x3B64, 0);
        clip.Cinematic = true;
        clip.SceneMotion = true;
        world.CinematicClips.Add(clip.Key, clip);
        var document = new SceneDocument();
        document.Worlds.Add(world);
        document.CinematicClips.Add(clip.Key, clip);
        AddList(document, List(0x100, 0x10, 0x150,
        [
            TransformChannel(0, 0x200, 0, 3) with
            {
                Calls = [(.5f, new CinematicLiteralCall(0x200, 8115, [0, 0x00600000]))]
            }
        ]));

        var playback = CinematicPlayback.Discover(document, world, new()).Single();
        playback.Activate();
        playback.Seek(1);
        if (MathF.Abs(target.PreviewMatrix!.Value.M41 - 3) > .001f)
            throw new InvalidDataException("CinematicPlayback must apply a started nested transform to its target.");
        playback.Seek(1.49f);
        if (!target.IsControlHidden)
            throw new InvalidDataException("Nested SceneEvents control flags must remain active before their cinematic timestamp.");
        playback.Seek(1.5f);
        if (target.IsControlHidden)
            throw new InvalidDataException("CinematicPlayback must apply nested control-flag clears on the shared clock.");

        string path = Path.Combine(outputDirectory, "cinematic-nested-eve-transform.glb");
        SceneExporter.Export(path, world, new(), new()
        {
            Animations = true,
            UnknownObjects = true,
            GameHidden = true,
            CinematicClipKey = clip.Key,
            CinematicDocument = document
        });
        using var glb = new GlbReader(path);
        int targetNode = Enumerable.Range(0, glb.Root.GetProperty("nodes").GetArrayLength())
            .Single(index => glb.Root.GetProperty("nodes")[index].GetProperty("name").GetString() == target.Name);
        int animationIndex = Enumerable.Range(0, glb.Root.GetProperty("animations").GetArrayLength())
            .Single(index => glb.Root.GetProperty("animations")[index].GetProperty("name").GetString() == clip.Name);
        Matrix4x4 exportedPose = glb.World(targetNode, animationIndex, 1);
        if (MathF.Abs(exportedPose.M41 - 3) > .001f)
            throw new InvalidDataException("Synchronized GLB export must include nested SceneEvents transform tracks at their cinematic time.");
        Matrix4x4 visibleExportPose = glb.World(targetNode, animationIndex, 1.5f);
        Matrix4x4 preActivationPose = glb.World(targetNode, animationIndex, MathF.BitDecrement(1.5f));
        if (MathF.Abs(visibleExportPose.M11) < .001f
            || MathF.Abs(preActivationPose.M11) > .001f)
            throw new InvalidDataException("Synchronized GLB export must apply nested control-flag visibility at the event's cinematic time.");

        var eventList = document.CinematicEmbeddedEventTracks[(world.Key, 0x100)].Single();
        var movingChannel = eventList.Tracks[0] with
        {
            Duration = 1,
            SourceTrack = new AnimationTrack
            {
                Bone = -1,
                Keys =
                [
                    new AnimationKey { Time = 0, Duration = 1, Position = Vector3.Zero },
                    new AnimationKey { Time = 1, Position = new Vector3(3, 0, 0) }
                ]
            }
        };
        document.CinematicEmbeddedEventTracks[(world.Key, 0x100)] =
            [eventList with { Tracks = [movingChannel] }];
        clip.Tracks[0].Keys.Add(CinematicTestEvents.Call(1.6f, 0x100, 0x3B61)); // TRAStopAll
        SceneExporter.Export(path, world, new(), new()
        {
            Animations = true,
            UnknownObjects = true,
            GameHidden = true,
            CinematicClipKey = clip.Key,
            CinematicDocument = document
        });
        using var stoppedGlb = new GlbReader(path);
        int stoppedTargetNode = Enumerable.Range(0, stoppedGlb.Root.GetProperty("nodes").GetArrayLength())
            .Single(index => stoppedGlb.Root.GetProperty("nodes")[index].GetProperty("name").GetString() == target.Name);
        int stoppedAnimationIndex = Enumerable.Range(0, stoppedGlb.Root.GetProperty("animations").GetArrayLength())
            .Single(index => stoppedGlb.Root.GetProperty("animations")[index].GetProperty("name").GetString() == clip.Name);
        Matrix4x4 poseAtStop = stoppedGlb.World(stoppedTargetNode, stoppedAnimationIndex, 1.6f);
        Matrix4x4 poseAfterStop = stoppedGlb.World(stoppedTargetNode, stoppedAnimationIndex, 1.9f);
        if (MathF.Abs(poseAtStop.M41 - 1.8f) > .03f
            || MathF.Abs(poseAfterStop.M41 - poseAtStop.M41) > .001f)
            throw new InvalidDataException("GLB export must preserve the interpolated EVE transform at a mid-track stop.");
        playback.Deactivate();
    }

    private static WorldData World(uint worldKey, params uint[] objectKeys)
    {
        var world = new WorldData { Key = worldKey };
        foreach (uint key in objectKeys) world.Objects.Add(new() { GaoKey = key, Name = $"object {key:X8}" });
        return world;
    }

    private static CinematicEmbeddedEventTrackList List(uint ownerKey, uint worldKey, uint eventKey,
        CinematicEmbeddedEventTrack[] tracks) => new(ownerKey, $"owner {ownerKey:X8}", worldKey,
            "SceneEvents", eventKey, tracks);

    private static void AddList(SceneDocument document, CinematicEmbeddedEventTrackList list) =>
        document.CinematicEmbeddedEventTracks[(list.WorldKey, list.OwnerKey)] = [list];

    private static CinematicEmbeddedEventTrack TransformChannel(int index, uint targetKey, float start, float x) =>
        new(index, targetKey, -1, 0, 1, 1) { SourceTrack = TransformSource(x, start) };

    private static AnimationTrack TransformSource(float x, float keyTime = 0) => new()
    {
        Bone = -1,
        Keys = [new AnimationKey { Time = keyTime, Position = new Vector3(x, 0, 0) }]
    };

    private static AnimationClip Clip(uint ownerKey, float callTime, int selector, params uint[] arguments) => new()
    {
        Key = 0x500,
        Name = "synthetic cinematic",
        Duration = 5,
        CinematicWorldKey = 0x10,
        Tracks = [new AnimationTrack
        {
            Bone = -1,
            Target = ownerKey,
            Keys = [CinematicTestEvents.Call(callTime, ownerKey, selector, arguments)]
        }]
    };

    private static AnimationTrack[] Read(WorldData world, SceneDocument document, AnimationClip clip) =>
        CinematicNestedEventTransformTracks.Read(clip, world, document);
}
