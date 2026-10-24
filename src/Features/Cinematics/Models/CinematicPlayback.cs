using System.Numerics;

namespace JadeView;

public sealed partial class CinematicPlayback
{
    private readonly Dictionary<SceneActor, AnimationClip> actorClips;
    private readonly ObjectTrack[] objectTracks;
    private readonly Dictionary<uint, AnimationTrack[]> sceneObjectTracksByTarget;
    private readonly uint[] scriptTrackTargets;
    private readonly FacialPlayback[] facialPlaybacks;
    private readonly ResourceResolver resolver;
    private readonly CinematicObjectState objectState;
    private readonly CinematicOrientToTimeline orientTo;
    private readonly CinematicSightSetTimeline sightSet;
    private readonly CinematicLocalRotationTimeline localRotations;
    private readonly CinematicGameScriptMoveTimeline gameScriptMoves;
    private readonly CinematicShutterTimeline shutter;
    private readonly CinematicScreenFadeTimeline screenFade;
    private readonly CinematicAfterEffectTimeline afterEffects;
    private readonly CinematicVideoPlaybackTimeline videoPlayback;
    private readonly CinematicAttackTimeline attack;
    private readonly CinematicShockwaveTimeline shockwave;
    private readonly CinematicFlashTimeline flashes;
    private readonly CinematicLightningTimeline lightnings;
    private readonly CinematicCameraPagTimeline cameraPags;
    private readonly CinematicParticleState particles;
    private readonly CinematicLightStateTimeline lightState;
    private readonly CinematicWorldCycleLightState worldCycleLightState;
    private readonly WorldData world;
    private CinematicLightStateEvaluation? evaluatedLightState;
    private CinematicScriptHierarchyPoseFrame[] scriptHierarchyPoseFrames = [];
    private CinematicScriptHierarchyCommand[] scriptHierarchyPoseCommands = [];
    private IReadOnlyDictionary<uint, CinematicScriptHierarchyPoseStatus> scriptHierarchyPoseStatuses =
        new Dictionary<uint, CinematicScriptHierarchyPoseStatus>();
    private Dictionary<uint, CinematicScriptHierarchyParent> scriptHierarchySerializedParents = [];
    private Dictionary<uint, int> scriptHierarchyEndpointCounts = [];
    private Dictionary<uint, SceneObject> scriptHierarchyUniqueObjects = [];
    private uint[] scriptHierarchyAffectedKeys = [];
    private readonly CinematicEventTrackPreview eventTrackPreview = new();
    private readonly Dictionary<SceneObject, Matrix4x4?> actorOrientPreviewSnapshots = [];
    private long allocatedByObjectState;
    private long allocatedByParticles;
    private long allocatedByActors;
    private long allocatedByEffects;
    private int allocationFrameCount;
    private bool captureAllocationDiagnostics;
    private long[] allocatedByActor = [];
    internal bool CaptureAllocationDiagnostics
    {
        get => captureAllocationDiagnostics;
        set
        {
            captureAllocationDiagnostics = value;
            allocatedByActor = value ? new long[actorClips.Count] : [];
            foreach (var actor in actorClips.Keys) actor.CaptureAllocationDiagnostics = value;
        }
    }
    internal string AllocationDiagnostics
    {
        get
        {
            double frameCount = Math.Max(1, allocationFrameCount);
            return $"object={allocatedByObjectState / frameCount:F0}B, "
                + $"particles={allocatedByParticles / frameCount:F0}B, actors={allocatedByActors / frameCount:F0}B, "
                + $"effects={allocatedByEffects / frameCount:F0}B per frame";
        }
    }
    internal string ActorAllocationDiagnostics => string.Join(", ", actorClips.Keys
        .Select((actor, index) => (Actor: actor.Root.Name, Bytes: allocatedByActor[index] /
            (double)Math.Max(1, allocationFrameCount)))
        .OrderByDescending(item => item.Bytes).Take(5)
        .Select(item => $"{item.Actor}={item.Bytes:F0}B/frame"));
    internal string ActorPhaseAllocationDiagnostics => actorClips.Keys
        .Select((actor, index) => (Actor: actor, Bytes: allocatedByActor[index]))
        .OrderByDescending(item => item.Bytes)
        .FirstOrDefault().Actor?.AllocationDiagnostics ?? "none";
    internal int BodyBaselineRefreshes => actorClips.Keys.Sum(actor => actor.CinematicBodyBaselineRefreshCount);
    internal void ResetAllocationDiagnostics()
    {
        allocatedByObjectState = 0;
        allocatedByParticles = 0;
        allocatedByActors = 0;
        allocatedByEffects = 0;
        allocationFrameCount = 0;
        Array.Clear(allocatedByActor);
        foreach (var actor in actorClips.Keys) actor.ResetAllocationDiagnostics();
    }

    private CinematicPlayback(AnimationClip sourceClip,
        AnimationTrack[] sourceTracks, int eventTrackTransformCount,
        CinematicScriptOwnerId[] callbackSceneEventOwners,
        CinematicScriptOwnerId[] previewableSceneEventOwners,
        CinematicOwnerExecutionLedgerEntry[] ownerExecutionLedger,
        CinematicOwnerChannelExecution[] ownerChannelExecutions,
        CinematicScheduledMessage[] scheduledMessages,
        CinematicEventRouteDiagnostic[] eventRouteDiagnostics,
        (float Time, CinematicLiteralCall Call)[] nestedEventCommands,
        (float Time, CinematicTimelineMessage Message)[] nestedEventMessages,
        Dictionary<SceneActor, AnimationClip> actorClips, ObjectTrack[] objectTracks,
        FacialPlayback[] facialPlaybacks, CinematicOrientToTimeline orientTo,
        CinematicSightSetTimeline sightSet,
        CinematicLocalRotationTimeline localRotations,
        CinematicGameScriptMoveTimeline gameScriptMoves,
        ResourceResolver resolver, WorldData world, SceneDocument document,
        IReadOnlyList<Entry>? archiveEntries)
    {
        SourceClip = sourceClip;
        ScriptCalls = CinematicLiteralCall.ReadTimeline(sourceClip);
        ScriptStateCalls = ScriptCalls.Concat(nestedEventCommands)
            .DistinctBy(item => (item.Time, item.Call.Target, item.Call.Selector,
                Arguments: string.Join(",", item.Call.Arguments)))
            .OrderBy(item => item.Time)
            .ToArray();
        ScriptMessages = CinematicTimelineMessage.ReadTimeline(sourceClip)
            .Concat(nestedEventMessages)
            .DistinctBy(item => item)
            .OrderBy(item => item.Time)
            .ToArray();
        scriptTrackTargets = sourceTracks
            .Select(track => track.Target)
            .Where(key => key is > 0 and < uint.MaxValue)
            .Distinct()
            .ToArray();
        this.actorClips = actorClips;
        this.objectTracks = objectTracks;
        sceneObjectTracksByTarget = CinematicHierarchyTransformSampler.Index(
            objectTracks.SelectMany(track => track.Tracks));
        this.orientTo = orientTo;
        this.sightSet = sightSet;
        this.localRotations = localRotations;
        this.gameScriptMoves = gameScriptMoves;
        EventTrackTransformCount = eventTrackTransformCount;
        CallbackSceneEventOwners = callbackSceneEventOwners;
        PreviewableSceneEventOwners = previewableSceneEventOwners;
        OwnerExecutionLedger = ownerExecutionLedger;
        OwnerChannelExecutions = ownerChannelExecutions;
        ScheduledMessages = scheduledMessages;
        EventRouteDiagnostics = eventRouteDiagnostics;
        this.facialPlaybacks = facialPlaybacks;
        this.resolver = resolver;
        this.world = world;
        videoPlayback = new(sourceClip, world, document, resolver, nestedEventCommands);
        objectState = new(sourceClip, world, actorClips.Keys, nestedEventCommands, videoPlayback);
        CinematicEnvironmentController[] environmentControllers = world.CinematicEnvironmentControllers.Length > 0
            ? world.CinematicEnvironmentControllers
            : document.CinematicEnvironmentControllers.Values
                .Where(controller => controller.WorldKey == world.Key).ToArray();
        attack = new(sourceClip, world, environmentControllers);
        shockwave = new(sourceClip, world, document.CinematicShockwaveControllers);
        flashes = new(sourceClip, world, document.CinematicFlashControllers);
        lightnings = new(sourceClip, world, document.CinematicLightningControllers);
        particles = new(sourceClip, world, resolver, nestedEventCommands);
        lightState = new(world.Objects, ScriptStateCalls);
        worldCycleLightState = new(environmentControllers, world);
        shutter = new(sourceClip, world, document);
        screenFade = new(sourceClip, world, document);
        afterEffects = new(sourceClip);
        MaterialState = new(sourceClip, world, resolver.Materials,
            environmentControllers, nestedEventCommands);
        AudioCues = CinematicAudioTimeline.Build(sourceClip, world, document, archiveEntries,
            out var audioDiagnostics, nestedEventCommands, ScriptMessages);
        AudioDiagnostics = audioDiagnostics;
        Duration = sourceClip.Duration;
        var fallbackCameras = objectTracks.Select(track => track.Object)
            .Concat(actorClips.Keys.Select(actor => actor.Root))
            .Where(sceneObject => sceneObject.Name.Contains("camera", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Cameras = CinematicCameraBinding.Discover(sourceClip, world.Objects, fallbackCameras,
            nestedEventCommands);
        cameraPags = new(document.CinematicScriptOwners.Values, world, ScriptStateCalls);
    }

    internal AnimationClip SourceClip { get; }
    internal (float Time, CinematicLiteralCall Call)[] ScriptCalls { get; }
    internal (float Time, CinematicLiteralCall Call)[] ScriptStateCalls { get; }
    internal (float Time, CinematicTimelineMessage Message)[] ScriptMessages { get; }
    internal IReadOnlyList<uint> ScriptTrackTargets => scriptTrackTargets;
    internal IEnumerable<SceneObject> VisibilityTargets => objectState.VisibilityTargets;
    internal int EventTrackTransformCount { get; }
    internal CinematicScriptOwnerId[] CallbackSceneEventOwners { get; }
    internal CinematicScriptOwnerId[] PreviewableSceneEventOwners { get; }
    internal CinematicOwnerExecutionLedgerEntry[] OwnerExecutionLedger { get; }
    internal CinematicOwnerChannelExecution[] OwnerChannelExecutions { get; }
    internal CinematicScheduledMessage[] ScheduledMessages { get; }
    internal CinematicEventRouteDiagnostic[] EventRouteDiagnostics { get; }
    public uint Key => SourceClip.Key;
    internal uint WorldKey => SourceClip.CinematicWorldKey;
    internal float Time { get; private set; }
    internal float Duration { get; }
    internal bool Playing { get; private set; }
    internal IReadOnlyCollection<SceneActor> Actors => actorClips.Keys;
    internal IEnumerable<(SceneActor Actor, CinematicBodyTimeline Body)> BodyTimelines =>
        actorClips.Where(pair => pair.Value.CinematicBody != null)
            .Select(pair => (pair.Key, pair.Value.CinematicBody!));
    internal string Name => SourceClip.Name;
    internal int ParticipantCount => actorClips.Count + objectTracks.Length;
    internal IEnumerable<string> ParticipantNames => actorClips.Keys.Select(actor => actor.Root.Name)
        .Concat(objectTracks.Select(track => track.Object.Name));
    internal IEnumerable<string> BodyDiagnostics => actorClips
        .Where(pair => pair.Key.Bones.Count > 0)
        .SelectMany(pair => new[] { $"{pair.Key.Root.Name}: {pair.Value.CinematicBody?.ActionCount ?? 0} body actions" }
            .Concat(pair.Value.CinematicBody?.Diagnostics ?? []));
    internal IReadOnlyList<string> TransformDiagnostics => orientTo.Diagnostics
        .Concat(sightSet.Diagnostics)
        .Concat(localRotations.Diagnostics)
        .Concat(gameScriptMoves.Diagnostics).Distinct(StringComparer.Ordinal).ToArray();
    internal CinematicCameraBinding[] Cameras { get; }
    internal CinematicMaterialState MaterialState { get; }
    internal CinematicLightStateEvaluation LightState => evaluatedLightState ?? lightState.Evaluate(Time);
    internal CinematicVideoPlaybackTimeline VideoPlayback => videoPlayback;
    internal float OffsetEffect => afterEffects.OffsetEffect;
    internal float ColorBalance => afterEffects.ColorBalance;
    internal int CameraIndex { get; set; }
    internal CinematicCameraBinding? Camera => Cameras.ElementAtOrDefault(CameraIndex);
    public CinematicAudioCue[] AudioCues { get; }
    internal string[] AudioDiagnostics { get; }
    internal CinematicShutterState? Shutter => shutter.Evaluate(Time);
    internal IReadOnlyList<CinematicFlashState> Flashes => flashes.Evaluate(Time);
    internal IReadOnlyList<CinematicLightningState> Lightnings => lightnings.Evaluate(Time);
    internal int ShutterCount => shutter.TriggerCount;
    internal System.Numerics.Vector4? ScreenFade => screenFade.Evaluate(Time);
    internal string[] ScreenFadeEvents => screenFade.Describe();

    public static CinematicPlayback[] Discover(SceneDocument document, WorldData world, ResourceResolver resolver,
        IReadOnlyList<Entry>? archiveEntries = null) =>
        Discover(document, world, resolver, archiveEntries, null);

    internal static CinematicPlayback[] Discover(SceneDocument document, WorldData world, ResourceResolver resolver,
        IReadOnlyList<Entry>? archiveEntries, IReadOnlySet<CinematicScriptOwnerId>? disabledCallbackOwners,
        IReadOnlySet<CinematicScriptOwnerId>? previewedSceneEventOwners = null,
        IReadOnlyDictionary<CinematicScriptOwnerId, float>? previewedSceneEventStartTimes = null) =>
        CinematicSceneClips.ForWorld(document, world)
            .Select(sourceClip => CreatePlayer(sourceClip, document, world, resolver, archiveEntries,
                disabledCallbackOwners, previewedSceneEventOwners, previewedSceneEventStartTimes))
            .Where(player => player != null)
            .Cast<CinematicPlayback>()
            .OrderBy(player => player.Key)
            .ToArray();

    internal static CinematicPlayback? DiscoverClip(AnimationClip sourceClip, SceneDocument document,
        WorldData world, ResourceResolver resolver,
        IReadOnlySet<CinematicScriptOwnerId>? disabledCallbackOwners,
        IReadOnlySet<CinematicScriptOwnerId>? previewedSceneEventOwners,
        IReadOnlyDictionary<CinematicScriptOwnerId, float>? previewedSceneEventStartTimes) =>
        CreatePlayer(sourceClip, document, world, resolver, null, disabledCallbackOwners,
            previewedSceneEventOwners, previewedSceneEventStartTimes);

    private static CinematicPlayback? CreatePlayer(AnimationClip sourceClip, SceneDocument document,
        WorldData world, ResourceResolver resolver, IReadOnlyList<Entry>? archiveEntries,
        IReadOnlySet<CinematicScriptOwnerId>? disabledCallbackOwners,
        IReadOnlySet<CinematicScriptOwnerId>? previewedSceneEventOwners,
        IReadOnlyDictionary<CinematicScriptOwnerId, float>? previewedSceneEventStartTimes)
    {
        var actorClips = new Dictionary<SceneActor, AnimationClip>();
        var nestedEventTimeline = CinematicNestedEventTransformTracks.ReadTimeline(sourceClip, world, document,
            disabledCallbackOwners, previewedSceneEventOwners, previewedSceneEventStartTimes);
        bool hasScriptHierarchyCommands = CinematicLiteralCall.ReadTimeline(sourceClip)
            .Concat(nestedEventTimeline.Commands)
            .Any(item => CinematicScriptHierarchyCommand.Decode(item.Time, item.Call) != null);
        var eventTrackTransforms = nestedEventTimeline.TransformTracks;
        var sourceTracks = sourceClip.Tracks
            .Concat(CinematicScriptTransformTracks.Read(sourceClip, nestedEventTimeline.Commands))
            .Concat(eventTrackTransforms)
            .ToArray();
        var moveCalls = CinematicLiteralCall.ReadTimeline(sourceClip)
            .Concat(nestedEventTimeline.Commands);
        var absolutePositionTrackTargets = sourceTracks
            .Where(track => track.Bone < 0 && track.Keys.Any(key => key.Position.HasValue))
            .Select(track => track.Target).ToHashSet();
        var gameScriptMoves = new CinematicGameScriptMoveTimeline(world,
            nestedEventTimeline.MoveSegments, moveCalls, absolutePositionTrackTargets,
            nestedEventTimeline.MoveDiagnostics);
        var orientTo = new CinematicOrientToTimeline(sourceClip, world,
            nestedEventTimeline.Commands, sourceTracks);
        var sightSet = new CinematicSightSetTimeline(world, moveCalls, sourceTracks);
        var localRotations = new CinematicLocalRotationTimeline(world,
            nestedEventTimeline.RotationCommands);
        foreach (var actor in world.Actors)
        {
            var actorTracks = sourceTracks.Where(track => IsActorTrack(track, actor)).ToList();
            var companionActionRequests = CinematicCompanionActionRequestResolver.Resolve(
                nestedEventTimeline.Commands, actor, world.Key, document.CinematicScriptOwners.Values);
            var bodyEventCalls = nestedEventTimeline.Commands.Concat(companionActionRequests.PreviewCalls).ToArray();
            var body = CinematicBodyTimeline.Create(sourceClip, actor, world.Key,
                document.CinematicActionKitsByOwner, bodyEventCalls);
            body.AddDiagnostics(companionActionRequests.Diagnostics);
            if (actorTracks.Count == 0 && body.ActionCount == 0) continue;
            actorClips[actor] = new AnimationClip
            {
                Key = sourceClip.Key,
                Name = sourceClip.Name,
                Duration = sourceClip.Duration,
                Tracks = actorTracks,
                Loaded = true,
                SceneMotion = true,
                Cinematic = true,
                CinematicBody = body,
                Unsupported = sourceClip.Unsupported
            };
        }

        var orientParticipants = orientTo.Participants.ToHashSet();
        var sightSetParticipants = sightSet.Participants.ToHashSet();
        var objectTracks = world.Objects.Where(sceneObject => sceneObject.Actor == null)
            .Select(sceneObject => new ObjectTrack(sceneObject, sourceTracks
                .Where(track => track.Bone < 0 && track.Target == sceneObject.GaoKey).ToArray()))
            .Where(track => track.Tracks.Length > 0 || orientParticipants.Contains(track.Object)
                || sightSetParticipants.Contains(track.Object)
                || localRotations.Targets.Contains(track.Object)
                || gameScriptMoves.Targets.Contains(track.Object))
            .ToArray();
        bool hasInspectableCallbackOwners = nestedEventTimeline.CallbackSceneEventOwners.Length > 0;
        if (actorClips.Count + objectTracks.Length < 2 && !orientTo.HasCommands && !sightSet.HasCommands
            && !hasInspectableCallbackOwners && !hasScriptHierarchyCommands
            && gameScriptMoves.Targets.Count == 0) return null;

        var facialPlaybacks = actorClips.Keys.Select(actor =>
                CreateFacialPlayback(actor, sourceClip, world.Objects, nestedEventTimeline.Messages,
                    document.CinematicScriptOwners.Values, world.Key))
            .Where(playback => playback != null)
            .Cast<FacialPlayback>()
            .ToArray();
        return new(sourceClip, sourceTracks, eventTrackTransforms.Length,
            nestedEventTimeline.CallbackSceneEventOwners, nestedEventTimeline.PreviewableSceneEventOwners,
            nestedEventTimeline.OwnerExecutionLedger,
            nestedEventTimeline.OwnerChannelExecutions,
            nestedEventTimeline.ScheduledMessages,
            nestedEventTimeline.RouteDiagnostics,
            nestedEventTimeline.Commands, nestedEventTimeline.Messages,
            actorClips, objectTracks, facialPlaybacks, orientTo,
            sightSet, localRotations, gameScriptMoves, resolver, world, document, archiveEntries);
    }

    private static bool IsActorTrack(AnimationTrack track, SceneActor actor)
    {
        if (track.Bone >= 0) return track.Target == actor.Root.GaoKey;
        return actor.Members.Any(member => member.GaoKey == track.Target);
    }

    public void Activate()
    {
        eventTrackPreview.Clear();
        actorOrientPreviewSnapshots.Clear();
        foreach (SceneObject source in orientTo.OrientedObjects
                     .Where(item => item.Actor != null && !actorClips.ContainsKey(item.Actor)))
            actorOrientPreviewSnapshots[source] = source.PreviewMatrix;
        foreach (var (actor, clip) in actorClips)
        {
            actor.CinematicClockOwned = true;
            actor.SelectClip(clip);
            actor.Playing = false;
            actor.Loop = false;
        }
        foreach (var playback in facialPlaybacks)
            playback.Previews = CreatePreviewSnapshots(playback.Actor, resolver);
        foreach (var track in objectTracks) track.Object.PreviewMatrix = null;
        localRotations.ResetTargets();
        BuildScriptHierarchyPoseFrames();
        Seek(0);
    }

    public void Play()
    {
        if (Duration <= 0) return;
        if (Time >= Duration) Seek(0);
        Playing = true;
    }

    public void Pause() => Playing = false;

    public void Deactivate()
    {
        Pause();
        ClearFacialOverlays();
        eventTrackPreview.Clear();
        foreach (var actor in actorClips.Keys) actor.CinematicClockOwned = false;
        foreach (var track in objectTracks) track.Object.PreviewMatrix = null;
        foreach (var (source, previewMatrix) in actorOrientPreviewSnapshots)
            source.PreviewMatrix = previewMatrix;
        actorOrientPreviewSnapshots.Clear();
        ClearScriptHierarchyWorldMatrices();
        scriptHierarchyPoseFrames = [];
        scriptHierarchyPoseCommands = [];
        scriptHierarchyPoseStatuses = new Dictionary<uint, CinematicScriptHierarchyPoseStatus>();
        scriptHierarchyPreviewObjects = [];
        scriptHierarchySampleKeys = [];
        scriptHierarchySerializedParents.Clear();
        scriptHierarchyEndpointCounts.Clear();
        scriptHierarchyUniqueObjects.Clear();
        scriptHierarchyAffectedKeys = [];
        cameraPags.Clear();
        objectState.Clear();
        particles.Clear();
        shockwave.Clear();
        evaluatedLightState = null;
    }

    internal bool PreviewEventTrack(SceneObject target, AnimationTrack track)
    {
        if (!eventTrackPreview.Start(target, track, Time)) return false;
        EvaluateActors();
        RefreshLightState();
        return true;
    }

    internal bool IsPreviewingEventTrack(SceneObject target, AnimationTrack track) =>
        eventTrackPreview.IsPreviewing(target, track);

    internal void ClearEventTrackPreview()
    {
        eventTrackPreview.Clear();
        EvaluateActors();
        RefreshLightState();
    }

    public void Restart()
    {
        Pause();
        Seek(0);
    }

    public void Advance(float delta)
    {
        if (!Playing || !float.IsFinite(delta) || delta <= 0) return;
        Time = Math.Min(Time + delta, Math.Max(Duration, 0));
        long allocationStart = CaptureAllocationDiagnostics ? GC.GetAllocatedBytesForCurrentThread() : 0;
        objectState.Evaluate(Time);
        if (CaptureAllocationDiagnostics) allocatedByObjectState += GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        allocationStart = CaptureAllocationDiagnostics ? GC.GetAllocatedBytesForCurrentThread() : 0;
        particles.Evaluate(Time);
        if (CaptureAllocationDiagnostics) allocatedByParticles += GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        var shockwaves = shockwave.Evaluate(Time);
        allocationStart = CaptureAllocationDiagnostics ? GC.GetAllocatedBytesForCurrentThread() : 0;
        EvaluateActors();
        cameraPags.Evaluate(Time, Camera?.RenderPose(Time));
        if (CaptureAllocationDiagnostics) allocatedByActors += GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        allocationStart = CaptureAllocationDiagnostics ? GC.GetAllocatedBytesForCurrentThread() : 0;
        float attackFactor = attack.Evaluate(Time);
        MaterialState.Evaluate(Time, attackFactor, shockwaves);
        RefreshLightState(attackFactor);
        afterEffects.Evaluate(Time);
        AdvanceFacialPlayback(Time);
        if (CaptureAllocationDiagnostics) allocatedByEffects += GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        if (CaptureAllocationDiagnostics) allocationFrameCount++;
        if (Time >= Duration) Pause();
    }

    public void Seek(float time)
    {
        Time = Math.Clamp(time, 0, Math.Max(Duration, 0));
        objectState.Evaluate(Time);
        particles.Evaluate(Time);
        var shockwaves = shockwave.Evaluate(Time);
        ResetFacialPlayback();
        EvaluateActors();
        cameraPags.Evaluate(Time, Camera?.RenderPose(Time));
        float attackFactor = attack.Evaluate(Time);
        MaterialState.Evaluate(Time, attackFactor, shockwaves);
        RefreshLightState(attackFactor);
        afterEffects.Evaluate(Time);
        AdvanceFacialPlayback(Time);
    }

    private void EvaluateObject(SceneObject sceneObject, AnimationTrack[] tracks, float time) =>
        sceneObject.PreviewMatrix = AnimationMath.FromRender(
            CinematicHierarchyTransformSampler.EvaluateWorldRender(sceneObject,
                sceneObjectTracksByTarget, time, world.Objects, tracks));

    private void RefreshLightState(float? attackFactor = null)
    {
        float resolvedAttackFactor = attackFactor ?? attack.Evaluate(Time);
        CinematicWorldCycleLightEvaluation cycleLights = worldCycleLightState.Evaluate(resolvedAttackFactor);
        evaluatedLightState = CinematicWorldCycleLightState.Compose(lightState.Evaluate(Time), cycleLights);
    }

    private sealed record ObjectTrack(SceneObject Object, AnimationTrack[] Tracks);
}
