using System.Globalization;
using JadeView;

internal static class CinematicCameraScriptFovAudit
{
    internal static void Write(string archivePath, string container, uint clipKey, uint ownerKey,
        float sampleTime, string output)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(item => item.Id == container);
        var document = SceneLoader.Load(archive.Read(entry), entry.Name, archivePath);
        AnimationClip sourceClip = document.CinematicClips[clipKey];
        WorldData world = CinematicWorldAssembler.Assemble(document,
            document.Worlds.Single(item => item.Key == sourceClip.CinematicWorldKey));
        var owner = document.CinematicScriptOwners.Values.Single(item => item.OwnerKey == ownerKey);
        var previewedOwners = new HashSet<CinematicScriptOwnerId> { owner.Id };
        var previewStarts = new Dictionary<CinematicScriptOwnerId, float> { [owner.Id] = 0 };
        var timeline = CinematicNestedEventTransformTracks.ReadTimeline(sourceClip, world, document,
            previewedSceneEventOwners: previewedOwners,
            previewedSceneEventStartTimes: previewStarts);
        var focalCall = timeline.Commands.FirstOrDefault(item =>
            CinematicEngineFunctionCatalog.NameFor(item.Call.Selector) == "VIEWFocaleSet"
            && item.Call.Arguments.Length == 2 && item.Call.Arguments[0] == 0);
        if (focalCall.Call == null)
            throw new InvalidDataException($"Selected owner {ownerKey:X8} has no literal VIEWFocaleSet for view 0.");

        float expectedAngle = BitConverter.UInt32BitsToSingle(focalCall.Call.Arguments[1]);
        if (!float.IsFinite(expectedAngle) || expectedAngle <= 0 || expectedAngle >= MathF.PI)
            throw new InvalidDataException($"Invalid source focal angle {expectedAngle}.");

        var resolver = new ResourceResolver();
        resolver.LoadFixContainer(archive);
        resolver.LoadSceneContainer(archive, entry);
        resolver.ResolveSceneDocument(document);
        CinematicPlayback playback = CinematicPlayback.Discover(document, world, resolver, archive.Entries,
            disabledCallbackOwners: null, previewedSceneEventOwners: previewedOwners,
            previewedSceneEventStartTimes: previewStarts)
            .Single(item => item.Key == clipKey);
        playback.Activate();
        try
        {
            var matchingCamera = playback.Cameras.FirstOrDefault(camera =>
                camera.ReceiverKey == focalCall.Call.Target && camera.Lens != null);
            if (matchingCamera?.Lens == null
                || MathF.Abs(matchingCamera.Lens.Evaluate(sampleTime) - expectedAngle) > .0001f
                || sampleTime < focalCall.Time)
                throw new InvalidDataException("Selected SceneEvents VIEWFocaleSet did not reach the matching preview camera lens.");

            Directory.CreateDirectory(output);
            string glbPath = Path.Combine(output, $"{container}-{clipKey:X8}-{ownerKey:X8}-camera-fov.glb");
            var result = SceneExporter.Export(glbPath, world, resolver, new()
            {
                Animations = true,
                CinematicDocument = document,
                CinematicClipKey = clipKey,
                PreviewedCinematicSceneEventOwners = previewedOwners,
                PreviewedCinematicSceneEventStartTimes = previewStarts
            });
            using var glb = new GlbReader(glbPath);
            bool exportedFocalChange = false;
            foreach (var camera in glb.Root.GetProperty("cameras").EnumerateArray())
            {
                if (!camera.TryGetProperty("extras", out var extras)
                    || !extras.TryGetProperty("jadeViewLensTracks", out var lensTracks)) continue;
                foreach (var lensTrack in lensTracks.EnumerateArray().Where(track =>
                             track.GetProperty("clipKey").GetString() == clipKey.ToString("X8")))
                foreach (var key in lensTrack.GetProperty("keys").EnumerateArray())
                    exportedFocalChange |= Math.Abs(key.GetProperty("time").GetSingle() - focalCall.Time) < .0001f
                        && Math.Abs(key.GetProperty("angle").GetSingle() - expectedAngle) < .0001f;
            }
            if (!exportedFocalChange || result.Animations == 0)
                throw new InvalidDataException("Synchronized GLB did not retain the selected EVE camera lens change.");

            Console.WriteLine($"PASS: {owner.Name} VIEWFocaleSet(view 0, {expectedAngle.ToString("G", CultureInfo.InvariantCulture)} rad) "
                + $"at {focalCall.Time.ToString("F3", CultureInfo.InvariantCulture)}s reaches preview and GLB camera lens");
        }
        finally
        {
            playback.Deactivate();
        }
    }
}
