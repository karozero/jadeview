namespace JadeView;

public sealed partial class CinematicPlayback
{
    private readonly Dictionary<SceneActor, AnimationClip> actorClips;
    private readonly ObjectTrack[] objectTracks;
    private readonly uint[] scriptTrackTargets;
    private readonly FacialPlayback[] facialPlaybacks;
    private readonly ResourceResolver resolver;
    private readonly CinematicObjectState objectState;
    private readonly CinematicOrientToTimeline orientTo;
    private readonly CinematicLocalRotationTimeline localRotations;
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
    private readonly CinematicEventTrackPreview eventTrackPreview = new();
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
        (float Time, CinematicLiteralCall Call)[] nestedEventCommands,
        Dictionary<SceneActor, AnimationClip> actorClips, ObjectTrack[] objectTracks,
        FacialPlayback[] facialPlaybacks, CinematicOrientToTimeline orientTo,
        CinematicLocalRotationTimeline localRotations,
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
        ScriptMessages = CinematicTimelineMessage.ReadTimeline(sourceClip);
        scriptTrackTargets = sourceTracks
            .Select(track => track.Target)
            .Where(key => key is > 0 and < uint.MaxValue)
            .Distinct()
            .ToArray();
        this.actorClips = actorClips;
        this.objectTracks = objectTracks;
        this.orientTo = orientTo;
        this.localRotations = localRotations;
        EventTrackTransformCount = eventTrackTransformCount;
        CallbackSceneEventOwners = callbackSceneEventOwners;
        PreviewableSceneEventOwners = previewableSceneEventOwners;
        this.facialPlaybacks = facialPlaybacks;
        this.resolver = resolver;
        videoPlayback = new(sourceClip, world, document, resolver, nestedEventCommands);
        objectState = new(sourceClip, world, actorClips.Keys, nestedEventCommands);
        attack = new(sourceClip, world, document.CinematicEnvironmentControllers.Values);
        shockwave = new(sourceClip, world, document.CinematicShockwaveControllers);
        flashes = new(sourceClip, world, document.CinematicFlashControllers);
        lightnings = new(sourceClip, world, document.CinematicLightningControllers);
        particles = new(sourceClip, world, resolver);
        cameraPags = new(document.CinematicScriptOwners.Values, world, ScriptStateCalls);
        shutter = new(sourceClip, world, document);
        screenFade = new(sourceClip, world, document);
        afterEffects = new(sourceClip);
        MaterialState = new(sourceClip, world, resolver.Materials, document.CinematicEnvironmentControllers.Values);
        AudioCues = CinematicAudioTimeline.Build(sourceClip, world, document, archiveEntries,
            out var audioDiagnostics, nestedEventCommands);
        AudioDiagnostics = audioDiagnostics;
        Duration = sourceClip.Duration;
        var fallbackCameras = objectTracks.Select(track => track.Object)
            .Concat(actorClips.Keys.Select(actor => actor.Root))
            .Where(sceneObject => sceneObject.Name.Contains("camera", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Cameras = CinematicCameraBinding.Discover(sourceClip, world.Objects, fallbackCameras);
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
    public uint Key => SourceClip.Key;
    internal uint WorldKey => SourceClip.CinematicWorldKey;
    internal float Time { get; private set; }
    internal float Duration { get; }
    internal bool Playing { get; private set; }
    internal IReadOnlyCollection<SceneActor> Actors => actorClips.Keys;
    internal string Name => SourceClip.Name;
    internal int ParticipantCount => actorClips.Count + objectTracks.Length;
    internal IEnumerable<string> ParticipantNames => actorClips.Keys.Select(actor => actor.Root.Name)
        .Concat(objectTracks.Select(track => track.Object.Name));
    internal IEnumerable<string> BodyDiagnostics => actorClips
        .Where(pair => pair.Key.Bones.Count > 0)
        .SelectMany(pair => new[] { $"{pair.Key.Root.Name}: {pair.Value.CinematicBody?.ActionCount ?? 0} body actions" }
            .Concat(pair.Value.CinematicBody?.Diagnostics ?? []));
    internal IReadOnlyList<string> TransformDiagnostics => orientTo.Diagnostics
        .Concat(localRotations.Diagnostics).Distinct(StringComparer.Ordinal).ToArray();
    internal CinematicCameraBinding[] Cameras { get; }
    internal CinematicMaterialState MaterialState { get; }
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
        IReadOnlySet<CinematicScriptOwnerId>? previewedSceneEventOwners = null) =>
        CinematicSceneClips.ForWorld(document, world)
            .Select(sourceClip => CreatePlayer(sourceClip, document, world, resolver, archiveEntries,
                disabledCallbackOwners, previewedSceneEventOwners))
            .Where(player => player != null)
            .Cast<CinematicPlayback>()
            .OrderBy(player => player.Key)
            .ToArray();

    private static CinematicPlayback? CreatePlayer(AnimationClip sourceClip, SceneDocument document,
        WorldData world, ResourceResolver resolver, IReadOnlyList<Entry>? archiveEntries,
        IReadOnlySet<CinematicScriptOwnerId>? disabledCallbackOwners,
        IReadOnlySet<CinematicScriptOwnerId>? previewedSceneEventOwners)
    {
        var actorClips = new Dictionary<SceneActor, AnimationClip>();
        var nestedEventTimeline = CinematicNestedEventTransformTracks.ReadTimeline(sourceClip, world, document,
            disabledCallbackOwners, previewedSceneEventOwners);
        var eventTrackTransforms = nestedEventTimeline.TransformTracks;
        var sourceTracks = sourceClip.Tracks
            .Concat(CinematicScriptTransformTracks.Read(sourceClip))
            .Concat(eventTrackTransforms)
            .ToArray();
        var orientTo = new CinematicOrientToTimeline(sourceClip, world,
            nestedEventTimeline.Commands, sourceTracks);
        var localRotations = new CinematicLocalRotationTimeline(world,
            nestedEventTimeline.RotationCommands);
        foreach (var actor in world.Actors)
        {
            var actorTracks = sourceTracks.Where(track => IsActorTrack(track, actor)).ToList();
            var companionActionRequests = CinematicCompanionActionRequestResolver.Resolve(
                nestedEventTimeline.Commands, actor, world.Key, document.CinematicScriptOwners.Values);
            var bodyEventCalls = nestedEventTimeline.Commands.Concat(companionActionRequests).ToArray();
            var body = CinematicBodyTimeline.Create(sourceClip, actor, world.Key,
                document.CinematicActionKitsByOwner, bodyEventCalls);
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
        var objectTracks = world.Objects.Where(sceneObject => sceneObject.Actor == null)
            .Select(sceneObject => new ObjectTrack(sceneObject, sourceTracks
                .Where(track => track.Bone < 0 && track.Target == sceneObject.GaoKey).ToArray()))
            .Where(track => track.Tracks.Length > 0 || orientParticipants.Contains(track.Object)
                || localRotations.Targets.Contains(track.Object))
            .ToArray();
        bool hasInspectableCallbackOwners = nestedEventTimeline.CallbackSceneEventOwners.Length > 0;
        if (actorClips.Count + objectTracks.Length < 2 && !hasInspectableCallbackOwners) return null;

        var facialPlaybacks = actorClips.Keys.Select(actor =>
                CreateFacialPlayback(actor, sourceClip, world.Objects))
            .Where(playback => playback != null)
            .Cast<FacialPlayback>()
            .ToArray();
        return new(sourceClip, sourceTracks, eventTrackTransforms.Length,
            nestedEventTimeline.CallbackSceneEventOwners, nestedEventTimeline.PreviewableSceneEventOwners,
            nestedEventTimeline.Commands,
            actorClips, objectTracks, facialPlaybacks, orientTo,
            localRotations, resolver, world, document, archiveEntries);
    }

    private static bool IsActorTrack(AnimationTrack track, SceneActor actor)
    {
        if (track.Bone >= 0) return track.Target == actor.Root.GaoKey;
        return actor.Members.Any(member => member.GaoKey == track.Target);
    }

    public void Activate()
    {
        eventTrackPreview.Clear();
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
        eventTrackPreview.Clear();
        foreach (var actor in actorClips.Keys) actor.CinematicClockOwned = false;
        foreach (var track in objectTracks) track.Object.PreviewMatrix = null;
        cameraPags.Clear();
        objectState.Clear();
        particles.Clear();
        shockwave.Clear();
    }

    internal bool PreviewEventTrack(SceneObject target, AnimationTrack track)
    {
        if (!eventTrackPreview.Start(target, track, Time)) return false;
        EvaluateActors();
        return true;
    }

    internal bool IsPreviewingEventTrack(SceneObject target, AnimationTrack track) =>
        eventTrackPreview.IsPreviewing(target, track);

    internal void ClearEventTrackPreview()
    {
        eventTrackPreview.Clear();
        EvaluateActors();
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
        cameraPags.Evaluate(Time);
        if (CaptureAllocationDiagnostics) allocatedByActors += GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        allocationStart = CaptureAllocationDiagnostics ? GC.GetAllocatedBytesForCurrentThread() : 0;
        MaterialState.Evaluate(Time, attack.Evaluate(Time), shockwaves);
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
        cameraPags.Evaluate(Time);
        MaterialState.Evaluate(Time, attack.Evaluate(Time), shockwaves);
        afterEffects.Evaluate(Time);
        AdvanceFacialPlayback(Time);
    }

    private static void EvaluateObject(SceneObject sceneObject, AnimationTrack[] tracks, float time) =>
        sceneObject.PreviewMatrix = CinematicTransformSampler.Evaluate(sceneObject.Transform, tracks, time);

    private sealed record ObjectTrack(SceneObject Object, AnimationTrack[] Tracks);
}
