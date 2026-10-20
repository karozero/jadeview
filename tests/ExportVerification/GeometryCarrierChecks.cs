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

    private static JsonDocument ReadDocument(string path)
    {
        // Carrier-only GLBs legitimately have no BIN chunk.
        byte[] bytes = File.ReadAllBytes(path);
        return JsonDocument.Parse(bytes.AsMemory(20, BitConverter.ToInt32(bytes, 12)));
    }
}
