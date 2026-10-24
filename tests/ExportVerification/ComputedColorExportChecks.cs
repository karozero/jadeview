using System.Numerics;
using System.Reflection;

namespace JadeView;

/// <summary>
/// GLB-level checks for computed vertex paint export. These exercise the serialized
/// COLOR_0 values so renderer-style material math cannot regress only in the exporter.
/// </summary>
internal static class ComputedColorExportChecks
{
    internal static void Verify(string output)
    {
        var resolver = new ResourceResolver();
        var geometry = new Geometry
        {
            Key = 0x710,
            Positions = [Vector3.Zero, Vector3.UnitX, Vector3.UnitZ],
            Uvs = [Vector2.Zero, Vector2.UnitX, Vector2.UnitY],
            VertexColors = [0x80402010, 0xFF806040, 0x204080C0],
            Parts = [new(0, [0, 1, 2], [0, 1, 2])]
        };
        Register(resolver, "_geometries", geometry.Key, geometry);

        const uint modeZeroMaterial = 0x711;
        const uint modeSixMaterial = 0x712;
        const uint modeThreeMaterial = 0x713;
        Register(resolver, "_materials", modeZeroMaterial, CreateMaterial(0));
        Register(resolver, "_materials", modeSixMaterial, CreateMaterial(6));
        Register(resolver, "_materials", modeThreeMaterial, CreateMaterial(3));

        const uint useRli = 0x40;
        const uint useAmbient = 0x100;
        const uint dontUseAmbient2 = 0x00400000;
        const uint commonFlags = 0x01000008;
        var world = new WorldData
        {
            Name = "computed vertex colors",
            Lighting = new WorldLighting(0xff0a141e, 0xff32281e)
        };
        world.Objects.Add(CreateObject("Use RLI", modeZeroMaterial, commonFlags | useRli | useAmbient));
        world.Objects.Add(CreateObject("Ambient only", modeZeroMaterial, commonFlags | useAmbient));
        world.Objects.Add(CreateObject("Ambient channel one", modeZeroMaterial,
            commonFlags | useAmbient | dontUseAmbient2));
        world.Objects.Add(CreateObject("Ambient channel two", modeZeroMaterial, commonFlags | useAmbient));
        world.Objects.Add(CreateObject("Mode six double", modeSixMaterial, commonFlags | useRli));
        world.Objects.Add(CreateObject("Mode three raw", modeThreeMaterial, commonFlags | useRli | useAmbient));

        string path = Path.Combine(output, "computed-vertex-colors.glb");
        SceneExporter.Export(path, world, resolver, new() { Textures = false });
        using var glb = new GlbReader(path);

        CheckColors(glb, "Use RLI", [0x8072482e, 0xFFB2885E, 0x2072a8de],
            "Color mode 0 must combine raw vertex RLI and selected world ambient RGB.");
        CheckColors(glb, "Ambient only", [0x0032281e, 0x0032281e, 0x0032281e],
            "Without the UseRLI draw flag, computed diffuse must not include raw vertex paint.");
        CheckColors(glb, "Ambient channel one", [0x000a141e, 0x000a141e, 0x000a141e],
            "DontUseAmbient2 must select ambient channel one for computed color.");
        CheckColors(glb, "Ambient channel two", [0x0032281e, 0x0032281e, 0x0032281e],
            "The default ambient selection must use channel two.");
        CheckColors(glb, "Mode six double", [0x80804020, 0xFFFFC080, 0x2080ffff],
            "Color mode 6 must double computed RGB with saturation while retaining computed alpha.");
        CheckColors(glb, "Mode three raw", geometry.VertexColors,
            "Color mode 3 must keep raw vertex color and ignore computed ambient/RLI state.");
    }

    private static SceneObject CreateObject(string name, uint materialKey, uint drawFlags) => new()
    {
        Name = name,
        GeometryKey = 0x710,
        MaterialKey = materialKey,
        DrawFlags = drawFlags
    };

    private static ResolvedMaterial CreateMaterial(int colorMode)
    {
        byte[] bytes = new byte[56];
        BitConverter.GetBytes(5).CopyTo(bytes, 0);
        uint materialFlags = (uint)colorMode << 12;
        BitConverter.GetBytes(materialFlags).CopyTo(bytes, 40);
        return new ResolvedMaterial { Type = 5, SourceBytes = bytes };
    }

    private static void CheckColors(GlbReader glb, string meshName, uint[] expectedColors, string message)
    {
        var mesh = glb.Root.GetProperty("meshes").EnumerateArray()
            .Single(candidate => candidate.GetProperty("name").GetString() == meshName);
        var primitive = mesh.GetProperty("primitives")[0];
        var colors = glb.Accessor(primitive.GetProperty("attributes").GetProperty("COLOR_0").GetInt32());
        Check(colors.Length == expectedColors.Length, $"{message} Expected {expectedColors.Length} vertices, got {colors.Length}.");
        for (int vertex = 0; vertex < colors.Length; vertex++)
        {
            float[] expected = ToVector(expectedColors[vertex]);
            Check(colors[vertex].Zip(expected).All(pair => MathF.Abs(pair.First - pair.Second) < .0001f),
                $"{message} Vertex {vertex}: expected {expectedColors[vertex]:X8}, got {Pack(colors[vertex]):X8}.");
        }
    }

    private static float[] ToVector(uint packed) =>
        [(packed & 255) / 255f, (packed >> 8 & 255) / 255f,
            (packed >> 16 & 255) / 255f, (packed >> 24) / 255f];

    private static uint Pack(float[] color) =>
        (uint)Math.Clamp((int)MathF.Round(color[0] * 255), 0, 255)
        | (uint)Math.Clamp((int)MathF.Round(color[1] * 255), 0, 255) << 8
        | (uint)Math.Clamp((int)MathF.Round(color[2] * 255), 0, 255) << 16
        | (uint)Math.Clamp((int)MathF.Round(color[3] * 255), 0, 255) << 24;

    private static void Register<T>(ResourceResolver resolver, string field, uint key, T value) =>
        ((Dictionary<uint, T>)typeof(ResourceResolver).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(resolver)!).Add(key, value);

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidDataException(message);
    }
}
