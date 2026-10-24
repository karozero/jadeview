using System.Numerics;
using System.Reflection;
using JadeView;

internal static class CinematicMaterialExportChecks
{
    internal static void Verify(string output)
    {
        const uint materialKey = 0x47000001;
        var bytes = new byte[56];
        BitConverter.GetBytes(5).CopyTo(bytes, 0);
        BitConverter.GetBytes((ushort)0xf800).CopyTo(bytes, 38);
        BitConverter.GetBytes(0x00000400u | JadeMaterialFlags.Disabled).CopyTo(bytes, 40);
        var material = new ResolvedMaterial { Id = new(materialKey), Type = 5, SourceBytes = bytes };
        var geometry = new Geometry { Key = 901,
            Positions = [Vector3.Zero, Vector3.UnitX, Vector3.UnitZ],
            Parts = [new(0, [0, 1, 2], [])] };
        var resolver = new ResourceResolver();
        Register(resolver, "_geometries", geometry.Key, geometry);
        Register(resolver, "_materials", materialKey, material);
        var world = new WorldData();
        world.Objects.Add(new() { GaoKey = 7, Name = "Timed material fixture",
            MaterialKey = materialKey, GeometryKey = geometry.Key });
        var clip = new AnimationClip { Tracks = [new() { Target = 7, Bone = -1,
            Keys = [CinematicTestEvents.Call(1, 7, 15006, materialKey, 0, 1),
                CinematicTestEvents.Call(2, 7, 15008, materialKey, 0, 64)] }] };
        var state = new CinematicMaterialState(clip, world, resolver.Materials);
        state.Evaluate(2);
        var snapshot = state.CaptureLayers();
        state.Evaluate(0); // Export must retain the captured sample after a rewind.
        string path = Path.Combine(output, "cinematic-material-snapshot.glb");
        var result = SceneExporter.Export(path, world, resolver,
            new() { Textures = false, CinematicMaterialLayers = snapshot });
        using var glb = new GlbReader(path);
        float alpha = glb.Root.GetProperty("materials")[0].GetProperty("pbrMetallicRoughness")
            .GetProperty("baseColorFactor")[3].GetSingle();
        Require(MathF.Abs(alpha - 8f / 31) < .0001f,
            "GLB must use sampled cinematic enable/local-alpha state, even after preview rewind.");
        Require(JadeMaterialLayer.Read(bytes)[0].Disabled
            && JadeMaterialLayer.Read(bytes)[0].OverrideAlpha == 1,
            "Export must not alter archive-owned material bytes.");
        Require(result.Warnings.Any(warning => warning.Contains("sampled at")),
            "The static material sample must disclose the missing material animation channels.");
        var disabled = SceneExporter.Export(Path.Combine(output, "cinematic-material-disabled.glb"),
            world, resolver, new() { Textures = false, CinematicMaterialLayers = state.CaptureLayers() });
        Require(disabled.Meshes == 0 && disabled.EmptyObjects == 1,
            "A fully disabled sampled material must retain its named node without exporting a visible fallback mesh.");
        Console.WriteLine("PASS: frozen cinematic material layer export, local alpha and source isolation");
    }

    private static void Register<T>(ResourceResolver resolver, string field, uint key, T value) =>
        ((Dictionary<uint, T>)typeof(ResourceResolver).GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(resolver)!).Add(key, value);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
