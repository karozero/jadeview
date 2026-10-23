using System.Reflection;
using System.Numerics;
using System.Text.Json;
using JadeView;

internal static class GeometryCarrierChecks
{
    internal static void Verify(string output)
    {
        var resolver = new ResourceResolver();
        var geometry = new Geometry
        {
            Key = 901,
            Particles = new(),
            ParticlePart = 0,
            Parts = [new(0, [0, 1, 2], [0, 0, 0])]
        };
        var geometries = (Dictionary<uint, Geometry>)typeof(ResourceResolver)
            .GetField("_geometries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(resolver)!;
        geometries.Add(geometry.Key, geometry);
        var root = new SceneObject { Id = "carrier-root", Name = "Carrier root", GaoKey = 902 };
        var carrier = new SceneObject
        {
            Id = "carrier",
            Name = "Particle carrier",
            GaoKey = 903,
            GeometryKey = geometry.Key,
            ActorNode = new() { Parent = root.GaoKey }
        };
        var actor = new SceneActor { Root = root, Members = [root, carrier] };
        foreach (var member in actor.Members) member.Actor = actor;
        var world = new WorldData();
        world.Objects.AddRange(actor.Members);
        world.Actors.Add(actor);
        foreach (bool animated in new[] { false, true })
        {
            string path = Path.Combine(output, $"particle-carrier-{animated}.glb");
            SceneExporter.Export(path, world, resolver, new() { Animations = animated }, root);
            using var document = ReadDocument(path);
            var node = document.RootElement.GetProperty("nodes").EnumerateArray()
                .Single(candidate => candidate.GetProperty("name").GetString() == carrier.Name);
            if (node.TryGetProperty("mesh", out _) ||
                !node.GetProperty("extras").GetProperty("particleGenerator").GetBoolean())
                throw new Exception("Particle carrier must retain its node without exporting generated triangles.");
        }

        VerifyParticleSnapshot(output, resolver, geometries);

        root.Transform.Scale = Vector3.Zero;
        carrier.ActorNode!.Local = Matrix4x4.CreateTranslation(7, 0, 0);
        string collapsedPath = Path.Combine(output, "collapsed-parent.glb");
        SceneExporter.Export(collapsedPath, world, resolver, new() { Animations = true }, root);
        using (var document = ReadDocument(collapsedPath))
        {
            var node = document.RootElement.GetProperty("nodes").EnumerateArray()
                .Single(candidate => candidate.GetProperty("name").GetString() == carrier.Name);
            if (MathF.Abs(node.GetProperty("translation")[0].GetSingle() - 7) > .00001f)
                throw new Exception("Zero-scale parent lost the child's evaluated local pose.");
        }
        root.Transform.Scale = Vector3.One;
        // A malformed ordinary triangle must still fail rather than becoming an empty carrier.
        geometry.Particles = null;
        geometry.Parts = [new(0, [0], [0])];
        try
        {
            SceneExporter.Export(Path.Combine(output, "invalid-carrier.glb"), world, resolver,
                new() { Animations = true }, root);
        }
        catch (InvalidDataException)
        {
            Console.WriteLine("PASS: static/animated particle carriers survive export; invalid triangles still fail");
            return;
        }
        throw new Exception("Malformed triangle indices were silently discarded.");
    }

    private static void VerifyParticleSnapshot(string output, ResourceResolver resolver,
        Dictionary<uint, Geometry> geometries)
    {
        const uint snapshotGeometryKey = 904;
        var snapshotGeometry = new Geometry
        {
            Key = snapshotGeometryKey,
            Particles = new ParticleDefinition { Capacity = 2 },
            ParticleStart = 3,
            ParticlePart = 1,
            Positions =
            [
                new(0, 0, 0), new(1, 0, 0), new(0, 1, 0),
                new(2, 0, 0), new(2, 1, 0), new(3, 1, 0), new(3, 0, 0),
                new(4, 0, 0), new(4, 1, 0), new(5, 1, 0), new(5, 0, 0)
            ],
            Uvs = [new(0, 0), new(1, 0), new(0, 1), new(1, 1)],
            VertexColors = new uint[11],
            Parts =
            [
                new(0, [0, 1, 2], [0, 1, 2]),
                new(0, [5, 4, 3, 3, 6, 5, 9, 8, 7, 7, 10, 9], [2, 1, 0, 0, 3, 2, 2, 1, 0, 0, 3, 2])
            ]
        };
        geometries.Add(snapshotGeometryKey, snapshotGeometry);
        var particle = new SceneObject
        {
            Id = "particle-snapshot",
            Name = "Particle snapshot",
            GaoKey = 905,
            GeometryKey = snapshotGeometryKey,
            PreviewParticlePositions = (Vector3[])snapshotGeometry.Positions.Clone(),
            PreviewParticleColors = new uint[11],
            PreviewParticleTime = 2.75f
        };
        particle.PreviewParticleColors[3] = 0xFFFFFFFF;
        particle.PreviewParticleColors[4] = 0xFFFFFFFF;
        particle.PreviewParticleColors[5] = 0xFFFFFFFF;
        particle.PreviewParticleColors[6] = 0xFFFFFFFF;
        var world = new WorldData();
        world.Objects.Add(particle);
        var result = SceneExporter.Export(Path.Combine(output, "particle-snapshot.glb"), world, resolver,
            new() { Textures = false });
        using var glb = new GlbReader(Path.Combine(output, "particle-snapshot.glb"));
        var node = glb.Root.GetProperty("nodes").EnumerateArray()
            .Single(candidate => candidate.GetProperty("name").GetString() == particle.Name);
        var extras = node.GetProperty("extras");
        if (!extras.GetProperty("particleSnapshot").GetBoolean()
            || extras.GetProperty("particleSnapshotTimeSeconds").GetSingle() != 2.75f
            || extras.TryGetProperty("particleGenerator", out _))
            throw new Exception("A current PAG preview should be labeled as a snapshot, not an unrendered generator.");
        var primitives = glb.Root.GetProperty("meshes")[node.GetProperty("mesh").GetInt32()]
            .GetProperty("primitives");
        int indexCount = primitives.EnumerateArray()
            .Sum(primitive => glb.Accessor(primitive.GetProperty("indices").GetInt32()).Length);
        if (indexCount != 9 || primitives.GetArrayLength() != 2 || result.Meshes != 1
            || !result.Warnings.Any(warning => warning.Contains("static snapshot", StringComparison.Ordinal)))
            throw new Exception($"Snapshot export mismatch: indices={indexCount}, primitives={primitives.GetArrayLength()}, meshes={result.Meshes}, warnings={string.Join(" | ", result.Warnings)}.");
        Console.WriteLine("PASS: GLB exports the viewport PAG frame as a labeled static snapshot");
    }

    private static JsonDocument ReadDocument(string path)
    {
        // Carrier-only GLBs legitimately have no BIN chunk.
        byte[] bytes = File.ReadAllBytes(path);
        return JsonDocument.Parse(bytes.AsMemory(20, BitConverter.ToInt32(bytes, 12)));
    }
}
