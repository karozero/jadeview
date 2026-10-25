using System.Globalization;
using JadeView;

internal static class CinematicExportAudit
{
    internal static void Write(string archivePath, string container, uint clipKey, float time,
        string output, bool allClips = false)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(item => item.Id == container);
        var document = SceneLoader.Load(archive.Read(entry), entry.Name, archivePath);
        var source = document.CinematicClips[clipKey];
        var world = CinematicWorldAssembler.Assemble(document,
            document.Worlds.Single(item => item.Key == source.CinematicWorldKey));
        var resolver = new ResourceResolver();
        resolver.LoadFixContainer(archive);
        resolver.LoadSceneContainer(archive, entry);
        resolver.ResolveSceneDocument(document);
        var playback = CinematicPlayback.Discover(document, world, resolver).Single(item => item.Key == clipKey);
        playback.Activate();
        try
        {
            playback.Seek(time);
            var poses = world.Objects.ToDictionary(owner => owner, owner => owner.PreviewMatrix);
            string timestamp = time.ToString("F3", CultureInfo.InvariantCulture);
            string clipScope = allClips ? "all-clips" : clipKey.ToString("X8", CultureInfo.InvariantCulture);
            string path = Path.Combine(output, $"{container}-{clipScope}-{timestamp}.glb");
            var result = SceneExporter.Export(path, world, resolver, new()
            {
                Animations = true,
                AllClips = allClips,
                CinematicDocument = document,
                CinematicClipKey = allClips ? null : clipKey,
                CinematicPlayback = playback,
                CinematicMorphTimelines = playback.SampleFacialMorphTimelines(),
                CinematicMaterialLayers = playback.MaterialState.CaptureLayers(),
                CinematicLights = playback.LightState
            });
            using var glb = new GlbReader(path);
            if (result.Meshes == 0 || !glb.Root.TryGetProperty("animations", out var animations)
                || animations.GetArrayLength() == 0)
                throw new InvalidDataException("The cinematic export contains no geometry or animations.");
            VerifyCallbackVisibilityExport(glb, playback);
            var changedPoses = poses.Where(pair => !NearlyEqual(pair.Key.PreviewMatrix, pair.Value))
                .Take(8)
                .Select(pair => $"{pair.Key.Name} ({pair.Key.GaoKey:X8}): expected {pair.Value}, got {pair.Key.PreviewMatrix}")
                .ToArray();
            if (changedPoses.Length > 0)
                throw new InvalidDataException("Cinematic export changed preview matrices beyond tolerance: "
                    + string.Join("; ", changedPoses));
            Console.WriteLine($"CINEMATIC EXPORT: {path}; scope={(allClips ? "all clips" : clipKey.ToString("X8"))}; meshes={result.Meshes}; textures={result.Textures}; "
                + $"animations={result.Animations}; warnings={result.Warnings.Length}; previewMatricesRestored=true");
            foreach (string warning in result.Warnings) Console.WriteLine("EXPORT WARNING: " + warning);
        }
        finally { playback.Deactivate(); }
    }

    private static void VerifyCallbackVisibilityExport(GlbReader glb, CinematicPlayback playback)
    {
        SceneObject[] targets = playback.VisibilityTargets
            .Where(target => !SceneRenderer.IsCollision(target)).Distinct().ToArray();
        if (targets.Length == 0) return;

        var nodes = glb.Root.GetProperty("nodes");
        var animations = glb.Root.GetProperty("animations");
        int animationIndex = Enumerable.Range(0, animations.GetArrayLength())
            .FirstOrDefault(index => animations[index].TryGetProperty("name", out var name)
                && name.GetString() == playback.Name, -1);
        if (animationIndex < 0)
            throw new InvalidDataException($"GLB omitted the synchronized visibility clip {playback.Key:X8}.");
        var channels = animations[animationIndex].GetProperty("channels").EnumerateArray().ToArray();
        var animatedNodeByTarget = new Dictionary<SceneObject, int>();
        foreach (SceneObject target in targets)
        {
            int animatedNode = Enumerable.Range(0, nodes.GetArrayLength())
                .Where(index => VisibilityNodeMatches(nodes[index], target))
                .FirstOrDefault(index => channels.Any(channel =>
                    channel.GetProperty("target").TryGetProperty("node", out var node)
                    && node.GetInt32() == index), -1);
            if (animatedNode < 0)
                throw new InvalidDataException($"GLB omitted visibility animation for callback target "
                    + $"{target.Name} ({target.GaoKey:X8}).");
            animatedNodeByTarget[target] = animatedNode;
        }

        float restoreTime = playback.Time;
        try
        {
            foreach (float time in playback.VisibilityChangeTimes.Append(0)
                         .Where(time => time >= 0 && time <= playback.Duration).Distinct().Order())
            {
                playback.EvaluateVisibilityState(time);
                foreach ((SceneObject target, int node) in animatedNodeByTarget)
                {
                    var pose = glb.World(node, animationIndex, time);
                    bool exportedHidden = MathF.Abs(pose.M11) + MathF.Abs(pose.M12)
                        + MathF.Abs(pose.M13) + MathF.Abs(pose.M21) + MathF.Abs(pose.M22)
                        + MathF.Abs(pose.M23) + MathF.Abs(pose.M31) + MathF.Abs(pose.M32)
                        + MathF.Abs(pose.M33) < .0001f;
                    bool previewHidden = target.IsControlHidden
                        || (target.EffectiveDrawFlags & SceneObject.DrawVisibilityMask) == 0;
                    if (exportedHidden != previewHidden)
                        throw new InvalidDataException($"GLB visibility mismatch for {target.Name} "
                            + $"({target.GaoKey:X8}) at {time:F3}s.");
                }
            }
        }
        finally { playback.EvaluateVisibilityState(restoreTime); }
    }

    private static bool VisibilityNodeMatches(System.Text.Json.JsonElement node, SceneObject target)
    {
        if (target.Actor == null)
            return node.TryGetProperty("name", out var name) && name.GetString() == target.Name;
        if (!node.TryGetProperty("extras", out var extras)
            || !extras.TryGetProperty("gaoKey", out var key)
            || key.GetString() != target.GaoKey.ToString("X8")) return false;
        return node.TryGetProperty("mesh", out _)
            || extras.TryGetProperty("actorComponentVisual", out var visual) && visual.GetBoolean()
            || extras.TryGetProperty("actorVisibility", out var actorVisibility) && actorVisibility.GetBoolean();
    }

    private static bool NearlyEqual(System.Numerics.Matrix4x4? actual,
        System.Numerics.Matrix4x4? expected)
    {
        if (actual.HasValue != expected.HasValue) return false;
        if (actual is not { } actualMatrix || expected is not { } expectedMatrix) return true;
        const float tolerance = 1e-5f;
        return MathF.Abs(actualMatrix.M11 - expectedMatrix.M11) <= tolerance
            && MathF.Abs(actualMatrix.M12 - expectedMatrix.M12) <= tolerance
            && MathF.Abs(actualMatrix.M13 - expectedMatrix.M13) <= tolerance
            && MathF.Abs(actualMatrix.M14 - expectedMatrix.M14) <= tolerance
            && MathF.Abs(actualMatrix.M21 - expectedMatrix.M21) <= tolerance
            && MathF.Abs(actualMatrix.M22 - expectedMatrix.M22) <= tolerance
            && MathF.Abs(actualMatrix.M23 - expectedMatrix.M23) <= tolerance
            && MathF.Abs(actualMatrix.M24 - expectedMatrix.M24) <= tolerance
            && MathF.Abs(actualMatrix.M31 - expectedMatrix.M31) <= tolerance
            && MathF.Abs(actualMatrix.M32 - expectedMatrix.M32) <= tolerance
            && MathF.Abs(actualMatrix.M33 - expectedMatrix.M33) <= tolerance
            && MathF.Abs(actualMatrix.M34 - expectedMatrix.M34) <= tolerance
            && MathF.Abs(actualMatrix.M41 - expectedMatrix.M41) <= tolerance
            && MathF.Abs(actualMatrix.M42 - expectedMatrix.M42) <= tolerance
            && MathF.Abs(actualMatrix.M43 - expectedMatrix.M43) <= tolerance
            && MathF.Abs(actualMatrix.M44 - expectedMatrix.M44) <= tolerance;
    }
}
