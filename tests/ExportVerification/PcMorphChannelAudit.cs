using System.Numerics;
using System.Text.Json;
using JadeView;

internal static class PcMorphChannelAudit
{
    private sealed record MorphGroupWeight(int Group, float Weight);
    private sealed record MorphProgressionSample(float Progression,
        MorphGroupWeight[] ActiveGroups, int ChangedVertices, float MaximumDelta);

    internal static void Write(string archivePath, string containerId, uint objectKey,
        string outputDirectory)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(item => item.Id == containerId);
        byte[] container = archive.Read(entry);
        SceneDocument document = SceneLoader.Load(container, entry.Name, archive.PathName);
        SceneObject[] matches = document.Worlds
            .SelectMany(world => world.Objects.Concat(world.Actors.SelectMany(actor => actor.Members)))
            .Where(sceneObject => sceneObject.GaoKey == objectKey)
            .Distinct()
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException($"Expected one object {objectKey:X8} in {containerId}, found {matches.Length}.");

        SceneObject sceneObject = matches[0];
        var resolver = new ResourceResolver();
        resolver.LoadFixContainer(archive);
        resolver.LoadSceneContainer(archive, entry);
        if (!resolver.Geometries.TryGetValue(sceneObject.RenderGeometryKey, out Geometry? geometry))
            throw new InvalidDataException($"Geometry {sceneObject.RenderGeometryKey:X8} was not resolved.");

        var modifiers = sceneObject.Modifiers.Where(modifier => modifier.Type == 4)
            .Select(modifier => FaceMorphReader.Read(modifier.Data, geometry.Positions.Length))
            .ToArray();
        if (modifiers.Length == 0)
            throw new InvalidDataException($"Object {objectKey:X8} has no type-4 morph modifier.");

        var report = new
        {
            Container = containerId,
            Object = sceneObject.Name,
            ObjectKey = objectKey.ToString("X8"),
            GeometryKey = geometry.Key.ToString("X8"),
            VertexCount = geometry.Positions.Length,
            Modifiers = modifiers.Select((data, modifierIndex) => new
            {
                Modifier = modifierIndex,
                data.Groups.Length,
                Channels = data.Channels.Select((channel, channelIndex) => new
                {
                    Channel = channelIndex,
                    SourceWeight = channel.Weight,
                    SourceProgression = channel.Progression,
                    References = channel.GroupReferences,
                    Samples = SampleProgressions(geometry, data, channelIndex)
                }).ToArray()
            }).ToArray(),
            Limits = "Progression sampling shows the deformation implied by JadeView's recovered channel evaluator. It does not label states as open/closed or prove retail-game visual parity."
        };

        Directory.CreateDirectory(outputDirectory);
        string outputPath = Path.Combine(outputDirectory,
            $"morph-channel-{objectKey:X8}-{geometry.Key:X8}.json");
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));

        Console.WriteLine($"MORPH CHANNEL AUDIT {sceneObject.Name} {objectKey:X8} geometry={geometry.Key:X8} vertices={geometry.Positions.Length}");
        foreach (var modifier in report.Modifiers)
        foreach (var channel in modifier.Channels)
        {
            Console.WriteLine($"MODIFIER {modifier.Modifier} CHANNEL {channel.Channel} source={channel.SourceWeight:F4}/{channel.SourceProgression:F4} refs=[{string.Join(',', channel.References)}]");
            foreach (var sample in channel.Samples)
                Console.WriteLine($"  progression={sample.Progression:F2} groups=[{string.Join(',', sample.ActiveGroups.Select(group => $"{group.Group}:{group.Weight:F3}"))}] changedVertices={sample.ChangedVertices} maxDelta={sample.MaximumDelta:F6}");
        }
        Console.WriteLine($"WROTE {outputPath}");
    }

    private static MorphProgressionSample[] SampleProgressions(Geometry geometry,
        FaceMorphData data, int channelIndex)
    {
        float[] progressions = [0, .5f, 1, 1.5f, 2, 2.5f];
        FaceMorphChannel sourceChannel = data.Channels[channelIndex];
        return progressions.Select(progression =>
        {
            var sampleChannel = new FaceMorphChannel(
                BitConverter.SingleToUInt32Bits(1),
                BitConverter.SingleToUInt32Bits(progression),
                sourceChannel.GroupReferences);
            float[] weights = FaceMorphChannelEvaluator.Evaluate(data.Groups.Length, [sampleChannel]);
            Vector3[] deltas = data.Groups.SelectMany(group => group.VertexIndices.Select((vertex, index) =>
                    (vertex, Delta: group.Deltas[index] * weights[group.Index])))
                .GroupBy(item => item.vertex)
                .Select(group => group.Aggregate(Vector3.Zero, (sum, item) => sum + item.Delta))
                .ToArray();
            MorphGroupWeight[] activeGroups = weights.Select((weight, group) => (weight, group))
                    .Where(item => item.weight != 0)
                    .Select(item => new MorphGroupWeight(item.group, item.weight)).ToArray();
            return new MorphProgressionSample(progression, activeGroups,
                deltas.Count(delta => delta.LengthSquared() > 1e-12f),
                deltas.Length == 0 ? 0 : deltas.Max(delta => delta.Length()));
        }).ToArray();
    }
}
