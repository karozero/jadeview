using System.Numerics;
using JadeView;

internal static class CinematicOrientToChecks
{
    internal static void Verify(string outputDirectory)
    {
        var source = new SceneObject { GaoKey = 10, Name = "Orient source" };
        var reference = new SceneObject
        {
            GaoKey = 20,
            Name = "Orient reference",
            Transform = new() { Translation = new Vector3(10, 0, 0) }
        };
        var world = new WorldData { Key = 30, Name = "orientation test" };
        world.Objects.AddRange([source, reference]);
        var clip = new AnimationClip
        {
            Key = 40,
            Name = "orient-to",
            Cinematic = true,
            SceneMotion = true,
            Loaded = true,
            CinematicWorldKey = world.Key,
            Duration = 2,
            Tracks = [new() { Bone = -1, Target = 0, Keys =
                [CinematicTestEvents.Call(1, source.GaoKey, 8151, reference.GaoKey)] }]
        };
        world.CinematicClips.Add(clip.Key, clip);
        var document = new SceneDocument();
        document.Worlds.Add(world);
        document.CinematicClips.Add(clip.Key, clip);

        var playback = CinematicPlayback.Discover(document, world, new()).Single();
        playback.Activate();
        playback.Seek(.5f);
        RequireNear(Vector3.TransformNormal(-Vector3.UnitY, source.PreviewMatrix!.Value),
            -Vector3.UnitY, "OBJ_OrientTo changed the source before its event time.");
        playback.Seek(1.5f);
        RequireNear(Vector3.TransformNormal(-Vector3.UnitY, source.PreviewMatrix!.Value),
            Vector3.UnitX, "OBJ_OrientTo did not orient the source's native -Y sight axis toward its reference.");
        Require(Vector3.Distance(source.PreviewMatrix!.Value.Translation, Vector3.Zero) < .001f,
            "OBJ_OrientTo changed the source object's position.");
        playback.Seek(.5f);
        RequireNear(Vector3.TransformNormal(-Vector3.UnitY, source.PreviewMatrix!.Value),
            -Vector3.UnitY, "Seeking before OBJ_OrientTo did not restore the authored orientation.");
        playback.Deactivate();

        string exportPath = Path.Combine(outputDirectory, "cinematic-orient-to.glb");
        SceneExporter.Export(exportPath, world, new(), new()
        {
            Animations = true,
            UnknownObjects = true,
            CinematicClipKey = clip.Key,
            CinematicDocument = document
        });
        using var glb = new GlbReader(exportPath);
        int sourceNode = Enumerable.Range(0, glb.Root.GetProperty("nodes").GetArrayLength())
            .Single(index => glb.Root.GetProperty("nodes")[index].GetProperty("name").GetString() == source.Name);
        int animationIndex = Enumerable.Range(0, glb.Root.GetProperty("animations").GetArrayLength())
            .Single(index => glb.Root.GetProperty("animations")[index].GetProperty("name").GetString() == clip.Name);
        Matrix4x4 exportedPose = glb.World(sourceNode, animationIndex, 1.5f);
        RequireNear(Vector3.TransformNormal(Vector3.UnitZ, exportedPose), Vector3.UnitX,
            "Cinematic GLB export did not preserve the scripted OBJ_OrientTo rotation.");
        Console.WriteLine("PASS: source-timed OBJ_OrientTo preview, reverse seek, position preservation and GLB export");
    }

    private static void RequireNear(Vector3 actual, Vector3 expected, string message)
    {
        if (Vector3.Distance(actual, expected) > .001f)
            throw new InvalidDataException($"{message} Actual {actual}; expected {expected}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
