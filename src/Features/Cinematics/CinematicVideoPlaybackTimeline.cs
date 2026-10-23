using System.Globalization;

namespace JadeView;

internal readonly record struct CinematicVideoPlaybackSample(bool Controlled, bool Ambiguous,
    bool HasStarted, bool IsPlaying, float VideoTime);

internal sealed class CinematicVideoPlaybackTimeline
{
    private const uint VideoPlayRequest = 0x100;
    private const uint VideoToggleRequest = 0x2;
    private const uint VideoStopRequest = 0x2000;
    private const uint VideoSwitchCapability = 0x100;
    private readonly Dictionary<uint, CinematicVideoPlaybackTrack[]> tracksByTexture;

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

        foreach (SceneObject sceneObject in world.Objects)
        {
            if (!scriptOwners.TryGetValue(sceneObject.GaoKey, out var matchingOwners)) continue;
            uint[] textureKeys = FindVideoTextureKeys(sceneObject, resolver).ToArray();
            if (textureKeys.Length == 0) continue;

            foreach (CinematicScriptOwner scriptOwner in matchingOwners)
            {
                uint? switchKey = scriptOwner.EffectiveObjectReference("des_o_Interrupteur")?.TargetKey;
                SceneObject? switchObject = ResolveUniqueObject(world, switchKey);
                bool initiallyPlaying = ReadInitialPlayingState(scriptOwner, switchObject);
                bool stopAtEnd = ReadStopAtEnd(scriptOwner);
                var controlEvents = ReadControlEvents(controlCalls, scriptOwner.OwnerKey, switchKey);
                var track = new CinematicVideoPlaybackTrack(initiallyPlaying, controlEvents, stopAtEnd);
                string trackIdentity = CreateTrackIdentity(scriptOwner.OwnerKey, switchKey,
                    initiallyPlaying, stopAtEnd, controlEvents);
                foreach (uint textureKey in textureKeys)
                {
                    if (!tracksByKey.TryGetValue(textureKey, out var tracks))
                        tracksByKey[textureKey] = tracks = new(StringComparer.Ordinal);
                    tracks.TryAdd(trackIdentity, track);
                }
            }
        }

        tracksByTexture = tracksByKey.ToDictionary(pair => pair.Key, pair => pair.Value.Values.ToArray());
    }

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

    internal static bool ReadInitialPlayingState(CinematicScriptOwner owner, SceneObject? switchObject)
    {
        string? value = owner.EffectiveVariable("i_Play")?.Value;
        bool serializedPlaying = int.TryParse(value, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int playing) && playing != 0;
        bool switchIsOn = switchObject != null
            && (switchObject.InitialCapabilities & VideoSwitchCapability) != 0;
        return serializedPlaying || switchIsOn;
    }

    private static bool ReadStopAtEnd(CinematicScriptOwner owner)
    {
        string? value = owner.EffectiveVariable("des_i_StopAtEnd")?.Value;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int stopAtEnd)
            && stopAtEnd != 0;
    }

    private static string CreateTrackIdentity(uint ownerKey, uint? switchKey, bool initiallyPlaying,
        bool stopAtEnd,
        CinematicVideoControlEvent[] events) =>
        $"{ownerKey:X8}:{switchKey?.ToString("X8", CultureInfo.InvariantCulture) ?? "none"}:"
        + $"{initiallyPlaying}:{stopAtEnd}:{string.Join(',', events.Select(item =>
            $"{item.Time.ToString("R", CultureInfo.InvariantCulture)}={item.Control}"))}";

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

internal sealed class CinematicVideoPlaybackTrack(bool initiallyPlaying,
    CinematicVideoControlEvent[] controlEvents, bool stopAtEnd = false)
{
    internal bool StopAtEnd { get; } = stopAtEnd;

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
        return new(true, false, hasStarted, isPlaying, videoTime);
    }
}
