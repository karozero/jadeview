using System.Numerics;
using System.Reflection;
using JadeView;

internal static class CinematicUvSampleChecks
{
    internal static void Verify(string output)
    {
        const uint geometryKey = 0x47008101;
        const uint materialKey = 0x47008102;
        var geometry = new Geometry
        {
            Key = geometryKey,
            Positions = [Vector3.Zero, Vector3.UnitX, Vector3.UnitZ],
            Uvs = [new(0, .5f)],
            Parts = [new(0, [0, 1, 2], [0, 0, 0])]
        };
        byte[] bytes = new byte[56];
        BitConverter.GetBytes(5).CopyTo(bytes, 0);
        BitConverter.GetBytes(JadeMaterialFlags.ScrollU | 7u).CopyTo(bytes, 40);
        // Scale 1, packed U scrolling speed .25; no V scrolling or rotation.
        BitConverter.GetBytes(0x3f803e80u).CopyTo(bytes, 44);
        BitConverter.GetBytes(0x3f800000u).CopyTo(bytes, 48);
        var resolver = new ResourceResolver();
        Register(resolver, "_geometries", geometryKey, geometry);
        Register(resolver, "_materials", materialKey, new ResolvedMaterial { Type = 5, SourceBytes = bytes });
        var world = new WorldData();
        world.Objects.Add(new SceneObject { GaoKey = 0x47008103, Name = "Scrolling", GeometryKey = geometryKey,
            MaterialKey = materialKey });
        var frozen = new CinematicMaterialLayerSnapshot(1, new Dictionary<(uint, int), JadeMaterialLayer>(),
            new Dictionary<(uint, int, int), byte>(), new Dictionary<(uint, int), Vector4>(),
            new Dictionary<uint, float>());
        string path = Path.Combine(output, "cinematic-uv-sample.glb");
        SceneExporter.Export(path, world, resolver, new() { Textures = false, CinematicMaterialLayers = frozen });
        using var glb = new GlbReader(path);
        int accessor = glb.Root.GetProperty("meshes")[0].GetProperty("primitives")[0]
            .GetProperty("attributes").GetProperty("TEXCOORD_0").GetInt32();
        if (glb.Accessor(accessor).Any(uv => MathF.Abs(uv[0] - .25f) > .0001f || MathF.Abs(uv[1] - .5f) > .0001f))
            throw new InvalidDataException("GLB scrolling UV must sample the frozen one-second playhead instead of time zero.");
        if (geometry.Uvs[0] != new Vector2(0, .5f))
            throw new InvalidDataException("Sampling UV motion changed the source geometry.");
        Console.WriteLine("PASS: scrolling UV export uses the frozen material playhead and preserves source UVs");
    }

    private static void Register<T>(ResourceResolver resolver, string field, uint key, T value) =>
        ((Dictionary<uint, T>)typeof(ResourceResolver).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(resolver)!).Add(key, value);
}
