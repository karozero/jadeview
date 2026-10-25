using System.Numerics;
using System.Text.Json;
using JadeView;

internal static class CinematicMorphChannelAudit
{
    private sealed record ActiveGroup(int Group, float Weight);
    private sealed record Sample(float Time, FaceMorphChannelState[] Channels,
        ActiveGroup[] ActiveGroups, int ChangedVertices, float MaximumDelta);

    internal static void Write(string archivePath, string containerId, uint clipKey,
        uint objectKey, float start, float end, float step, string outputDirectory,
        int? savedFlagIndex = null, bool? savedFlagValue = null)
    {
        if (!float.IsFinite(start) || !float.IsFinite(end) || !float.IsFinite(step)
            || start < 0 || end < start || step <= 0 || (end - start) / step > 10000)
            throw new ArgumentOutOfRangeException(nameof(step), "Use a finite positive sample interval and range.");
        if (savedFlagIndex.HasValue != savedFlagValue.HasValue)
            throw new ArgumentException("A saved-flag index and value must be supplied together.");

        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(item => item.Id == containerId);
        SceneDocument document = SceneLoader.Load(archive.Read(entry), entry.Name, archive.PathName);
        if (!document.CinematicClips.TryGetValue(clipKey, out AnimationClip? clip))
            throw new InvalidDataException($"Clip {clipKey:X8} was not found in {containerId}.");
        WorldData sourceWorld = document.Worlds.Single(world => world.Key == clip.CinematicWorldKey);
        WorldData world = CinematicWorldAssembler.Assemble(document, sourceWorld);
        var resolver = new ResourceResolver();
        resolver.LoadFixContainer(archive);
        resolver.LoadSceneContainer(archive, entry);
        resolver.ResolveSceneDocument(document);
        CinematicPlayback playback = CinematicPlayback.Discover(document, world, resolver,
                archive.Entries)
            .Single(candidate => candidate.Key == clipKey);
        SceneObject[] targetMatches = world.Objects
            .Concat(world.Actors.SelectMany(actor => actor.Members))
            .Where(sceneObject => sceneObject.GaoKey == objectKey)
            .Distinct().ToArray();
        if (targetMatches.Length != 1 || targetMatches[0].MorphPreview is not { } preview)
            throw new InvalidDataException($"Expected one morph-enabled object {objectKey:X8}; found {targetMatches.Length}.");
        Geometry geometry = resolver.Geometries[ targetMatches[0].RenderGeometryKey ];

        IReadOnlyDictionary<int, bool?> assumptions = savedFlagIndex is { } flagIndex
            ? new Dictionary<int, bool?> { [flagIndex] = savedFlagValue }
            : new Dictionary<int, bool?>();
        playback.BindSavedFlagAssumptions(assumptions);
        var samples = new List<Sample>();
        playback.Activate();
        try
        {
            int sampleCount = (int)MathF.Floor((end - start) / step) + 1;
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                float time = Math.Min(end, start + sampleIndex * step);
                playback.Seek(time);
                float[] weights = preview.CopyWeights();
                Vector3[] positions = preview.Positions();
                Vector3[] deltas = positions.Select((position, index) => position - geometry.Positions[index]).ToArray();
                FaceMorphChannelState[] channels = preview.CopyChannelState() ?? [];
                samples.Add(new(time, channels,
                    weights.Select((weight, group) => new ActiveGroup(group, weight))
                        .Where(group => group.Weight != 0).ToArray(),
                    deltas.Count(delta => delta.LengthSquared() > 1e-12f),
                    deltas.Length == 0 ? 0 : deltas.Max(delta => delta.Length())));
            }
        }
        finally
        {
            playback.Deactivate();
        }

        var report = new
        {
            Container = containerId,
            Clip = clipKey.ToString("X8"),
            Object = targetMatches[0].Name,
            ObjectKey = objectKey.ToString("X8"),
            GeometryKey = geometry.Key.ToString("X8"),
            SavedFlagAssumptions = assumptions,
            Samples = samples,
            Limits = "Samples are JadeView cinematic-preview state. They do not prove retail callback dispatch or assign semantic labels such as open/closed."
        };
        Directory.CreateDirectory(outputDirectory);
        string outputPath = Path.Combine(outputDirectory,
            $"cinematic-morph-{containerId}-{clipKey:X8}-{objectKey:X8}.json");
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        Console.WriteLine($"CINEMATIC MORPH CHANNEL AUDIT {targetMatches[0].Name} {objectKey:X8} geometry={geometry.Key:X8}");
        foreach (Sample sample in samples)
        {
            string channelValues = string.Join(",", sample.Channels.Select(channel =>
                $"{channel.Weight:F3}/{channel.Progression:F3}"));
            Console.WriteLine($"  t={sample.Time:F3} channels=[{channelValues}] groups=[{string.Join(',', sample.ActiveGroups.Select(group => $"{group.Group}:{group.Weight:F3}"))}] changedVertices={sample.ChangedVertices} maxDelta={sample.MaximumDelta:F6}");
        }
        Console.WriteLine($"WROTE {outputPath}");
    }
}
