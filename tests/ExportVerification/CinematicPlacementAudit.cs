using System.Numerics;
using System.Text.Json;
using JadeView;

internal static class CinematicPlacementAudit
{
    internal static void Write(string archivePath, string container, uint clipKey, string output)
    {
        var archive = new Archive(archivePath);
        var entry = archive.Entries.Single(entry => entry.Id == container);
        var document = SceneLoader.Load(archive.Read(entry), entry.Name, archivePath);
        var source = document.CinematicClips[clipKey];
        var world = CinematicWorldAssembler.Assemble(document,
            document.Worlds.Single(world => world.Key == source.CinematicWorldKey));
        var player = CinematicPlayback.Discover(document, world, new()).Single(player => player.Key == clipKey);
        Console.WriteLine($"ASSEMBLY: {world.Objects.Count} objects; {world.Objects.GroupBy(item => item.GaoKey).Count(group => group.Count() > 1)} duplicated GAO keys");
        foreach (var group in world.Objects.GroupBy(item => item.GaoKey).Where(group => group.Count() > 1))
            Console.WriteLine($"DUPLICATE {group.Key:X8}: {string.Join(", ", group.Select(item => item.Name))}");
        player.Activate();
        var reports = new List<object>();
        foreach (var actor in player.Actors.Where(actor => actor.Bones.Count > 0))
        {
            var samples = new List<(float Time, float Distance, Vector3 Position, bool SourcePlacement)>();
            var skeletonSamples = new List<object>();
            Vector3 previous = actor.Root.PreviewMatrix!.Value.Translation;
            float previousTime = 0;
            var placements = source.Tracks.Where(track => track.Bone < 0 && track.Target == actor.Root.GaoKey)
                .SelectMany(track => track.Keys).Where(key => key.Position != null).ToArray();
            for (int frame = 1; frame / 60f <= source.Duration; frame++)
            {
                float time = frame / 60f;
                actor.Time = time;
                actor.Evaluate(0);
                var position = actor.Root.PreviewMatrix!.Value.Translation;
                bool sourcePlacement = placements.Any(key => key.Time > previousTime && key.Time <= time);
                samples.Add((time, Vector3.Distance(position, previous), position, sourcePlacement));
                if (frame % 30 == 0)
                {
                    Matrix4x4.Decompose(actor.Root.PreviewMatrix!.Value, out var rootScale, out _, out _);
                    skeletonSamples.Add(new
                    {
                        Time = time,
                        Root = position,
                        RootScale = rootScale,
                        BodyFrame = actor.Clip?.CinematicBody?.Sample(time).Select(sample => (float?)(sample.Time * 60)).FirstOrDefault(),
                        DirectChildren = actor.Bones.Where(bone => bone != null && actor.PoseParent(bone) == actor.Root)
                            .Select(bone => new { bone!.Name, Position = bone.PreviewMatrix!.Value.Translation }).ToArray()
                    });
                }
                previous = position;
                previousTime = time;
            }
            reports.Add(new
            {
                Actor = actor.Root.Name,
                Key = actor.Root.GaoKey.ToString("X8"),
                BoneHierarchy = actor.Bones.Select(bone => bone == null ? null : new
                { bone.Name, Key = bone.GaoKey.ToString("X8"), Parent = bone.ActorNode?.Parent.ToString("X8") }).ToArray(),
                SkeletonSamples = skeletonSamples,
                LargestSteps = samples.OrderByDescending(sample => sample.Distance).Take(12).Select(sample => new
                {
                    sample.Time,
                    sample.Distance,
                    sample.Position,
                    sample.SourcePlacement
                }).ToArray()
            });
            Console.WriteLine($"PLACEMENT {clipKey:X8} {actor.Root.Name}: max step {samples.Max(sample => sample.Distance):F3}");
        }
        player.Deactivate();
        File.WriteAllText(Path.Combine(output, $"{container}-{clipKey:X8}-placement.json"),
            JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
    }
}
