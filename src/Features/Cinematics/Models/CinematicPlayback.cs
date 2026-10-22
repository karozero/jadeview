namespace JadeView;

public sealed partial class CinematicPlayback
{
    private readonly Dictionary<SceneActor, AnimationClip> actorClips;
    private readonly ObjectTrack[] objectTracks;
    private readonly uint[] scriptTrackTargets;
    private readonly FacialPlayback[] facialPlaybacks;
    private readonly ResourceResolver resolver;
    private readonly CinematicObjectState objectState;
    private readonly CinematicShutterTimeline shutter;
    private readonly CinematicScreenFadeTimeline screenFade;
    private readonly CinematicAttackTimeline attack;
    private readonly CinematicShockwaveTimeline shockwave;
    private readonly CinematicFlashTimeline flashes;
    private readonly CinematicLightningTimeline lightnings;

    private CinematicPlayback(AnimationClip sourceClip,
        Dictionary<SceneActor, AnimationClip> actorClips, ObjectTrack[] objectTracks,
        FacialPlayback[] facialPlaybacks, ResourceResolver resolver, WorldData world, SceneDocument document,
        IReadOnlyList<Entry>? archiveEntries)
    {
        SourceClip = sourceClip;
        ScriptCalls = CinematicLiteralCall.ReadTimeline(sourceClip);
        ScriptMessages = CinematicTimelineMessage.ReadTimeline(sourceClip);
        scriptTrackTargets = sourceClip.Tracks.Concat(CinematicScriptTransformTracks.Read(sourceClip))
            .Select(track => track.Target)
            .Where(key => key is > 0 and < uint.MaxValue)
            .Distinct()
            .ToArray();
        this.actorClips = actorClips;
        this.objectTracks = objectTracks;
        this.facialPlaybacks = facialPlaybacks;
        this.resolver = resolver;
        objectState = new(sourceClip, world, actorClips.Keys);
        attack = new(sourceClip, world, document.CinematicEnvironmentControllers.Values);
        shockwave = new(sourceClip, world, document.CinematicShockwaveControllers);
        flashes = new(sourceClip, world, document.CinematicFlashControllers);
        lightnings = new(sourceClip, world, document.CinematicLightningControllers);
        shutter = new(sourceClip, world, document);
        screenFade = new(sourceClip, world, document);
        MaterialState = new(sourceClip, world, resolver.Materials, document.CinematicEnvironmentControllers.Values);
        AudioCues = CinematicAudioTimeline.Build(sourceClip, world, document, archiveEntries, out var audioDiagnostics);
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
    internal (float Time, CinematicTimelineMessage Message)[] ScriptMessages { get; }
    internal IReadOnlyList<uint> ScriptTrackTargets => scriptTrackTargets;
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
    internal CinematicCameraBinding[] Cameras { get; }
    internal CinematicMaterialState MaterialState { get; }
    internal int CameraIndex { get; set; }
    internal CinematicCameraBinding? Camera => Cameras.ElementAtOrDefault(CameraIndex);
    public CinematicAudioCue[] AudioCues { get; }
    internal string[] AudioDiagnostics { get; }
    internal CinematicShutterState? Shutter => shutter.Evaluate(Time);
    internal CinematicFlashState[] Flashes => flashes.Evaluate(Time);
    internal CinematicLightningState[] Lightnings => lightnings.Evaluate(Time);
    internal int ShutterCount => shutter.TriggerCount;
    internal System.Numerics.Vector4? ScreenFade => screenFade.Evaluate(Time);

    public static CinematicPlayback[] Discover(SceneDocument document, WorldData world, ResourceResolver resolver,
        IReadOnlyList<Entry>? archiveEntries = null) =>
        CinematicSceneClips.ForWorld(document, world)
            .Select(sourceClip => CreatePlayer(sourceClip, document, world, resolver, archiveEntries))
            .Where(player => player != null)
            .Cast<CinematicPlayback>()
            .OrderBy(player => player.Key)
            .ToArray();

    private static CinematicPlayback? CreatePlayer(AnimationClip sourceClip, SceneDocument document,
        WorldData world, ResourceResolver resolver, IReadOnlyList<Entry>? archiveEntries)
    {
        var actorClips = new Dictionary<SceneActor, AnimationClip>();
        var sourceTracks = sourceClip.Tracks.Concat(CinematicScriptTransformTracks.Read(sourceClip)).ToArray();
        foreach (var actor in world.Actors)
        {
            var actorTracks = sourceTracks.Where(track => IsActorTrack(track, actor)).ToList();
            var body = CinematicBodyTimeline.Create(sourceClip, actor, world.Key,
                document.CinematicActionKitsByOwner);
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

        var objectTracks = world.Objects.Where(sceneObject => sceneObject.Actor == null)
            .Select(sceneObject => new ObjectTrack(sceneObject, sourceTracks
                .Where(track => track.Bone < 0 && track.Target == sceneObject.GaoKey).ToArray()))
            .Where(track => track.Tracks.Length > 0)
            .ToArray();
        if (actorClips.Count + objectTracks.Length < 2) return null;

        var facialPlaybacks = actorClips.Keys.Select(actor =>
                CreateFacialPlayback(actor, sourceClip, world.Objects))
            .Where(playback => playback != null)
            .Cast<FacialPlayback>()
            .ToArray();
        return new(sourceClip, actorClips, objectTracks, facialPlaybacks, resolver, world, document, archiveEntries);
    }

    private static bool IsActorTrack(AnimationTrack track, SceneActor actor)
    {
        if (track.Bone >= 0) return track.Target == actor.Root.GaoKey;
        return actor.Members.Any(member => member.GaoKey == track.Target);
    }

    public void Activate()
    {
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
        foreach (var actor in actorClips.Keys) actor.CinematicClockOwned = false;
        objectState.Clear();
        shockwave.Clear();
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
        objectState.Evaluate(Time);
        var shockwaves = shockwave.Evaluate(Time);
        EvaluateActors();
        MaterialState.Evaluate(Time, attack.Evaluate(Time), shockwaves);
        AdvanceFacialPlayback(Time);
        if (Time >= Duration) Pause();
    }

    public void Seek(float time)
    {
        Time = Math.Clamp(time, 0, Math.Max(Duration, 0));
        objectState.Evaluate(Time);
        var shockwaves = shockwave.Evaluate(Time);
        ResetFacialPlayback();
        EvaluateActors();
        MaterialState.Evaluate(Time, attack.Evaluate(Time), shockwaves);
        AdvanceFacialPlayback(Time);
    }

    private static void EvaluateObject(SceneObject sceneObject, AnimationTrack[] tracks, float time) =>
        sceneObject.PreviewMatrix = CinematicTransformSampler.Evaluate(sceneObject.Transform, tracks, time);

    private sealed record ObjectTrack(SceneObject Object, AnimationTrack[] Tracks);
}
