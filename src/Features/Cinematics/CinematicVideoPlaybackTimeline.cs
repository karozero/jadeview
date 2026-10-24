using System.Globalization;

namespace JadeView;

internal readonly record struct CinematicVideoPlaybackSample(bool Controlled, bool Ambiguous,
    bool HasStarted, bool IsPlaying, float VideoTime)
{
    internal bool InitialStateResolved { get; init; } = true;
    internal string InitialStateEvidence { get; init; } = string.Empty;
}

internal readonly record struct CinematicVideoInitialPlaybackState(
    bool InitiallyPlaying, bool IsResolved, string Evidence);

internal sealed class CinematicVideoPlaybackTimeline
{
    private const uint VideoPlayRequest = 0x100;
    private const uint VideoToggleRequest = 0x2;
    private const uint VideoStopRequest = 0x2000;
    private const uint VideoSwitchCapability = 0x100;
    private readonly Dictionary<uint, CinematicVideoPlaybackTrack[]> tracksByTexture;
    private readonly Dictionary<uint, CinematicVideoPlaybackTrack[]> tracksByOwner;

    internal int ControlledTextureCount => tracksByTexture.Count;
    internal IEnumerable<uint> TextureKeys => tracksByTexture.Keys;
    internal bool StopsAtMediaEnd(uint textureKey) => tracksByTexture.TryGetValue(textureKey, out var tracks)
        && tracks.Length == 1 && tracks[0].StopAtEnd;
    internal bool HasActiveVideo(float timelineTime) => tracksByTexture.Values
        .SelectMany(tracks => tracks)
        .Any(track =>
        {
            CinematicVideoPlaybackSample sample = track.Evaluate(timelineTime);
            return sample.HasStarted && sample.IsPlaying;
        });

    internal CinematicVideoPlaybackTimeline(AnimationClip clip, WorldData world,
        SceneDocument document, ResourceResolver resolver,
        IEnumerable<(float Time, CinematicLiteralCall Call)>? nestedEventCommands = null)
    {
        var controlCalls = CinematicLiteralCall.ReadTimeline(clip)
            .Concat(nestedEventCommands ?? [])
            .DistinctBy(item => (item.Time, item.Call.Target, item.Call.Selector,
                Arguments: string.Join(",", item.Call.Arguments)))
            .ToArray();
        var scriptOwners = document.CinematicScriptOwners.Values
            .Where(owner => owner.WorldKey == world.Key
                && owner.CallbackNames.Contains("mpegplayer_init", StringComparer.Ordinal))
            .GroupBy(owner => owner.OwnerKey)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var tracksByKey = new Dictionary<uint, Dictionary<string, CinematicVideoPlaybackTrack>>();
        var tracksByOwnerKey = new Dictionary<uint, Dictionary<string, CinematicVideoPlaybackTrack>>();

        foreach (SceneObject sceneObject in world.Objects)
        {
            if (!scriptOwners.TryGetValue(sceneObject.GaoKey, out var matchingOwners)) continue;
            uint[] textureKeys = FindVideoTextureKeys(sceneObject, resolver).ToArray();
            if (textureKeys.Length == 0) continue;

            foreach (CinematicScriptOwner scriptOwner in matchingOwners)
            {
                uint? switchKey = scriptOwner.EffectiveObjectReference("des_o_Interrupteur")?.TargetKey;
                SceneObject? switchObject = ResolveUniqueObject(world, switchKey);
                CinematicVideoInitialPlaybackState initialState = ResolveInitialPlaybackState(
                    scriptOwner, switchObject);
                bool stopAtEnd = ReadStopAtEnd(scriptOwner);
                var controlEvents = ReadControlEvents(controlCalls, scriptOwner.OwnerKey, switchKey);
                var track = new CinematicVideoPlaybackTrack(scriptOwner.OwnerKey, initialState,
                    controlEvents, stopAtEnd);
                string trackIdentity = CreateTrackIdentity(scriptOwner.OwnerKey, switchKey,
                    initialState, stopAtEnd, controlEvents);
                if (!tracksByOwnerKey.TryGetValue(scriptOwner.OwnerKey, out var ownerTracks))
                    tracksByOwnerKey[scriptOwner.OwnerKey] = ownerTracks = new(StringComparer.Ordinal);
                ownerTracks.TryAdd(trackIdentity, track);
                foreach (uint textureKey in textureKeys)
                {
                    if (!tracksByKey.TryGetValue(textureKey, out var tracks))
                        tracksByKey[textureKey] = tracks = new(StringComparer.Ordinal);
                    tracks.TryAdd(trackIdentity, track);
                }
            }
        }

        tracksByTexture = tracksByKey.ToDictionary(pair => pair.Key, pair => pair.Value.Values.ToArray());
        tracksByOwner = tracksByOwnerKey.ToDictionary(pair => pair.Key, pair => pair.Value.Values.ToArray());
    }

    internal CinematicVideoPlaybackTimeline(IEnumerable<CinematicVideoPlaybackTrack> ownerTracks)
    {
        tracksByTexture = [];
        tracksByOwner = ownerTracks.GroupBy(track => track.OwnerKey)
            .ToDictionary(group => group.Key, group => group.ToArray());
    }

    internal IEnumerable<(uint OwnerKey, bool Visible)> InitialOwnerVisibility => tracksByOwner
        .Where(pair => pair.Value.Length == 1 && pair.Value[0].InitialVisibility is not null)
        .Select(pair => (pair.Key, pair.Value[0].InitialVisibility!.Value));

    internal IEnumerable<(float Time, uint OwnerKey, bool Visible)> OwnerVisibilityEvents => tracksByOwner
        .Where(pair => pair.Value.Length == 1)
        .SelectMany(pair => pair.Value[0].VisibilityEvents.Select(change =>
            (change.Time, pair.Key, change.Visible)));

    internal CinematicVideoPlaybackSample? Evaluate(uint textureKey, float timelineTime)
    {
        if (!tracksByTexture.TryGetValue(textureKey, out var tracks)) return null;
        if (tracks.Length != 1) return new(true, true, false, false, 0);
        return tracks[0].Evaluate(timelineTime);
    }

    internal string Describe(uint textureKey, float timelineTime)
    {
        CinematicVideoPlaybackSample? sample = Evaluate(textureKey, timelineTime);
        if (sample == null) return "No MPEG-player callback is linked; sampling from cinematic time.";
        CinematicVideoPlaybackSample playbackSample = sample.Value;
        if (playbackSample.Ambiguous) return "Multiple MPEG-player callbacks control this texture; trigger timing is ambiguous.";
        if (!playbackSample.InitialStateResolved)
            return $"MPEG-player initial state is estimated: {playbackSample.InitialStateEvidence} "
                + "The preview uses serialized i_Play/switch state as a fallback; native callback dispatch is not observed.";
        if (!playbackSample.HasStarted) return "Waiting for an explicit cinematic video trigger.";
        return playbackSample.IsPlaying
            ? $"Script-triggered video · {playbackSample.VideoTime:F2}s from media start"
            : $"Video paused by script · frozen at {playbackSample.VideoTime:F2}s";
    }

    private static IEnumerable<uint> FindVideoTextureKeys(SceneObject sceneObject, ResourceResolver resolver)
    {
        if (sceneObject.MaterialKey is 0 or uint.MaxValue) yield break;

        var pendingMaterials = new Queue<uint>();
        var visitedMaterials = new HashSet<uint>();
        pendingMaterials.Enqueue(sceneObject.MaterialKey);
        while (pendingMaterials.TryDequeue(out uint materialKey))
        {
            if (!visitedMaterials.Add(materialKey)
                || !resolver.Materials.TryGetValue(materialKey, out var material)) continue;
            foreach (uint childKey in material.SubMaterialKeys)
                pendingMaterials.Enqueue(childKey);

            foreach (JadeMaterialLayer layer in JadeMaterialLayer.Read(material.SourceBytes))
                if (resolver.ProceduralTextures.TryGetValue(layer.Texture, out var procedural)
                    && procedural.Subtype == 3)
                    yield return procedural.Key;
        }
    }

    internal static CinematicVideoInitialPlaybackState ResolveInitialPlaybackState(
        CinematicScriptOwner owner, SceneObject? switchObject)
    {
        string? value = owner.EffectiveVariable("i_Play")?.Value;
        bool serializedPlaying = int.TryParse(value, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int playing) && playing != 0;
        bool switchIsOn = switchObject != null
            && (switchObject.InitialCapabilities & VideoSwitchCapability) != 0;
        bool fallbackPlaying = serializedPlaying || switchIsOn;

        int? specialFlagModifier = ReadEffectiveInteger(owner, "des_i_sf_modifier_vrai");
        int? specialFlagPresence = ReadEffectiveInteger(owner, "des_i_sf_jesuisla");
        int? savedFlagSwitch = ReadEffectiveInteger(owner, "des_i_sf_on_off");
        int? soundModifier = ReadEffectiveInteger(owner, "des_i_SND_modifier");
        if (specialFlagModifier is > 0 || specialFlagPresence is not 0 and not null)
            return Estimated("an alternate save-flag/timer branch may stop or delay the normal init path");
        if (specialFlagModifier is null || specialFlagPresence is null || savedFlagSwitch is null
            || soundModifier is null)
            return Estimated("one or more effective save-flag branch values are unavailable");
        if (savedFlagSwitch is > 0)
            return Estimated($"saved flag {savedFlagSwitch} can start playback independently of the switch object's capability");
        if (soundModifier is > 0)
            return Estimated($"sound modifier {soundModifier} can hide the video owner when its runtime status gate fails");

        CinematicScriptObjectReference? switchReference = owner.EffectiveObjectReference("des_o_Interrupteur");
        if (switchReference == null || switchObject == null
            || switchObject.GaoKey != switchReference.TargetKey)
            return Estimated("des_o_Interrupteur is absent, unresolved, or not unique in the preview world");

        // Iris's ordinary mpegplayer_init node (15) overwrites i_Play from the
        // switch capability before selecting the video material. Serialized
        // i_Play is therefore only a fallback when that source branch is unknown.
        return new(switchIsOn, true,
            $"normal init path: i_Play is overwritten from {switchObject.Name} ({switchObject.GaoKey:X8}) capability 0x100");

        CinematicVideoInitialPlaybackState Estimated(string reason) => new(fallbackPlaying, false, reason);
    }

    private static bool ReadStopAtEnd(CinematicScriptOwner owner)
    {
        string? value = owner.EffectiveVariable("des_i_StopAtEnd")?.Value;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int stopAtEnd)
            && stopAtEnd != 0;
    }

    private static string CreateTrackIdentity(uint ownerKey, uint? switchKey,
        CinematicVideoInitialPlaybackState initialState,
        bool stopAtEnd,
        CinematicVideoControlEvent[] events) =>
        $"{ownerKey:X8}:{switchKey?.ToString("X8", CultureInfo.InvariantCulture) ?? "none"}:"
        + $"{initialState.InitiallyPlaying}:{initialState.IsResolved}:{stopAtEnd}:{string.Join(',', events.Select(item =>
            $"{item.Time.ToString("R", CultureInfo.InvariantCulture)}={item.Control}"))}";

    private static int? ReadEffectiveInteger(CinematicScriptOwner owner, string name) =>
        int.TryParse(owner.EffectiveVariable(name)?.Value, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int value) ? value : null;

    internal static CinematicVideoControlEvent[] ReadControlEvents(
        IEnumerable<(float Time, CinematicLiteralCall Call)> calls, uint ownerKey, uint? switchKey) => calls
        .Where(item => CinematicEngineFunctionCatalog.NameFor(item.Call.Selector) == "OBJ_CapaSet"
            && item.Call.Arguments.Length >= 2)
        .Select(item => ReadControlEvent(item.Time, item.Call, ownerKey, switchKey))
        .OfType<CinematicVideoControlEvent>()
        .OrderBy(item => item.Time)
        .ToArray();

    private static CinematicVideoControlEvent? ReadControlEvent(float time,
        CinematicLiteralCall call, uint ownerKey, uint? switchKey)
    {
        uint addMask = call.Arguments[0];
        if (call.Target == ownerKey)
        {
            bool toggle = (addMask & VideoToggleRequest) != 0;
            bool explicitStart = (addMask & VideoPlayRequest) != 0;
            bool explicitStop = (addMask & VideoStopRequest) != 0;
            if (explicitStop) return new(time, CinematicVideoControl.Stop);
            if (toggle) return new(time, CinematicVideoControl.Toggle);
            if (explicitStart) return new(time, CinematicVideoControl.Start);
        }
        else if (switchKey == call.Target && (addMask & VideoSwitchCapability) != 0)
        {
            // Iris mpegplayer_loop treats the switch capability as a toggle request.
            return new(time, CinematicVideoControl.Toggle);
        }

        return null;
    }

    private static SceneObject? ResolveUniqueObject(WorldData world, uint? key)
    {
        if (key is null or 0 or uint.MaxValue) return null;
        SceneObject? resolved = null;
        foreach (var sceneObject in world.Objects)
        {
            if (sceneObject.GaoKey != key.Value) continue;
            if (resolved != null) return null;
            resolved = sceneObject;
        }
        return resolved;
    }

}

internal enum CinematicVideoControl { Start, Stop, Toggle }

internal sealed record CinematicVideoControlEvent(float Time, CinematicVideoControl Control);

internal sealed class CinematicVideoPlaybackTrack
{
    private readonly bool initiallyPlaying;
    private readonly bool initialStateResolved;
    private readonly string initialStateEvidence;
    private readonly CinematicVideoControlEvent[] controlEvents;
    private readonly (float Time, bool Visible)[] visibilityEvents;
    internal uint OwnerKey { get; }
    // The same fallback state drives both the video quad and its owner visibility.
    // IsResolved remains separate so the UI can disclose when native gates are unknown.
    internal bool? InitialVisibility => initiallyPlaying;
    internal IReadOnlyList<(float Time, bool Visible)> VisibilityEvents => visibilityEvents;
    internal bool StopAtEnd { get; }

    internal CinematicVideoPlaybackTrack(bool initiallyPlaying,
        CinematicVideoControlEvent[] controlEvents, bool stopAtEnd = false)
        : this(0, new CinematicVideoInitialPlaybackState(initiallyPlaying, true, "explicit preview state"),
            controlEvents, stopAtEnd)
    {
    }

    internal CinematicVideoPlaybackTrack(uint ownerKey, CinematicVideoInitialPlaybackState initialState,
        CinematicVideoControlEvent[] controlEvents, bool stopAtEnd = false)
    {
        OwnerKey = ownerKey;
        initiallyPlaying = initialState.InitiallyPlaying;
        initialStateResolved = initialState.IsResolved;
        initialStateEvidence = initialState.Evidence;
        this.controlEvents = controlEvents;
        StopAtEnd = stopAtEnd;
        visibilityEvents = controlEvents
            .Select(item => (item.Time, Evaluate(item.Time).IsPlaying)).ToArray();
    }

    internal CinematicVideoPlaybackSample Evaluate(float timelineTime)
    {
        bool isPlaying = initiallyPlaying;
        bool hasStarted = initiallyPlaying;
        float mediaStart = 0;
        float frozenTime = 0;
        for (int controlIndex = 0; controlIndex < controlEvents.Length
            && controlEvents[controlIndex].Time <= timelineTime; controlIndex++)
        {
            CinematicVideoControlEvent controlEvent = controlEvents[controlIndex];
            bool nextPlaying = controlEvent.Control switch
            {
                CinematicVideoControl.Start => true,
                CinematicVideoControl.Stop => false,
                CinematicVideoControl.Toggle => !isPlaying,
                _ => isPlaying
            };
            if (!isPlaying && nextPlaying)
            {
                mediaStart = controlEvent.Time;
                hasStarted = true;
            }
            else if (isPlaying && !nextPlaying)
            {
                frozenTime = Math.Max(0, controlEvent.Time - mediaStart);
            }
            isPlaying = nextPlaying;
        }

        float videoTime = isPlaying ? Math.Max(0, timelineTime - mediaStart) : frozenTime;
        return new(true, false, hasStarted, isPlaying, videoTime)
        {
            InitialStateResolved = initialStateResolved,
            InitialStateEvidence = initialStateEvidence
        };
    }
}
