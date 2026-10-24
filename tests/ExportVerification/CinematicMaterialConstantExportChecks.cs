using System.Drawing;
using System.Numerics;
using System.Reflection;
using JadeView;

internal static class CinematicMaterialConstantExportChecks
{
    internal static void Verify(string output)
    {
        VerifyFrozenWorldCycleAndShockwaveConstants();
        VerifyGlbConstantUsesObjectAndSubmaterialContext(output);
        VerifyBakedModeTwoTintUsesFrozenConstant(output);
        Console.WriteLine("PASS: frozen cinematic material constants, shockwave fade, and mode-2 export parity");
    }

    private static void VerifyFrozenWorldCycleAndShockwaveConstants()
    {
        const uint environmentKey = 0x51000020;
        const uint sunKey = 0x51000010;
        var world = new WorldData();
        world.Objects.AddRange([
            new SceneObject { GaoKey = sunKey, Transform = new() { Translation = new(0, 0, 4) } },
            new SceneObject { GaoKey = environmentKey },
            new SceneObject { GaoKey = 0x51000001 }
        ]);
        uint[][] palettes = Enumerable.Range(0, 4)
            .Select(paletteIndex => Enumerable.Range(0, 9)
                .Select(colorIndex => 0xff000000u | (uint)(paletteIndex + 1) << 16
                    | (uint)(paletteIndex + 1) << 8 | (uint)(paletteIndex * 16 + colorIndex)).ToArray())
            .ToArray();
        var controller = new CinematicEnvironmentController(0x51000001, world.Key, [sunKey])
        {
            EnvironmentKey = environmentKey,
            SunKey = sunKey,
            NightHeight = 0,
            DawnHeight = 1,
            SunriseHeight = 2,
            DayHeight = 3,
            NormalPalettes = palettes
        };
        var state = new CinematicMaterialState(new AnimationClip(), world,
            new Dictionary<uint, ResolvedMaterial>(), [controller]);
        var shockwave = new CinematicShockwaveTimeline.ShockwaveState(
            world.Objects[1], 0, 1, .25f, false);
        state.Evaluate(3, shockwaves: [shockwave]);
        CinematicMaterialLayerSnapshot snapshot = state.CaptureLayers();
        Vector4 expectedPaletteConstant = PackedColor(palettes[3][8]);
        Vector4 expectedFadedConstant = Vector4.Lerp(expectedPaletteConstant, Vector4.Zero, .25f);

        state.Evaluate(0);
        Vector4 frozenConstant = snapshot.ApplyConstant(environmentKey, 1, Vector4.One);
        Require(frozenConstant == expectedFadedConstant,
            "CaptureLayers must freeze the evaluated environment constant and its shockwave fade across later seeks.");
    }

    private static void VerifyGlbConstantUsesObjectAndSubmaterialContext(string output)
    {
        const uint objectKey = 0x51000030;
        const uint rootMaterialKey = 0x47000010;
        const uint firstMaterialKey = 0x47000011;
        const uint secondMaterialKey = 0x47000012;
        var resolver = new ResourceResolver();
        var geometry = new Geometry
        {
            Key = 0x47000020,
            Positions = [Vector3.Zero, Vector3.UnitX, Vector3.UnitZ],
            Parts = [new(0, [0, 1, 2], []), new(1, [0, 1, 2], [])]
        };
        Register(resolver, "_geometries", geometry.Key, geometry);
        Register(resolver, "_materials", firstMaterialKey, ConstantMaterial(firstMaterialKey, 0xff112233));
        Register(resolver, "_materials", secondMaterialKey, ConstantMaterial(secondMaterialKey, 0xff445566));
        var rootMaterial = new ResolvedMaterial { Id = new(rootMaterialKey), Type = 4 };
        rootMaterial.SubMaterialKeys.AddRange([firstMaterialKey, secondMaterialKey]);
        Register(resolver, "_materials", rootMaterialKey, rootMaterial);
        var world = new WorldData();
        world.Objects.Add(new SceneObject { GaoKey = objectKey, Name = "Environment", GeometryKey = geometry.Key,
            MaterialKey = rootMaterialKey });
        var snapshot = new CinematicMaterialLayerSnapshot(2, new Dictionary<(uint, int), JadeMaterialLayer>(),
            new Dictionary<(uint, int, int), byte>(),
            new Dictionary<(uint, int), Vector4> { [(objectKey, 1)] = new(0, .5f, 1, .8f) },
            new Dictionary<uint, float> { [objectKey] = .25f });

        string path = Path.Combine(output, "cinematic-mode2-constant.glb");
        SceneExporter.Export(path, world, resolver,
            new() { Textures = false, CinematicMaterialLayers = snapshot });
        using var glb = new GlbReader(path);
        var primitives = glb.Root.GetProperty("meshes")[0].GetProperty("primitives");
        float[] first = MaterialColor(glb, primitives[0]);
        float[] second = MaterialColor(glb, primitives[1]);
        Vector4 expectedFirst = Vector4.Lerp(new(0x33 / 255f, 0x22 / 255f, 0x11 / 255f, 1), Vector4.Zero, .25f);
        Vector4 expectedSecond = Vector4.Lerp(new(0, .5f, 1, .8f), Vector4.Zero, .25f);
        Require(Near(first, expectedFirst)
            && Near(second, expectedSecond),
            $"Mode-2 glTF factors must use RGB with baseline alpha 1, apply shockwave fade to submaterials 0/1, and use frozen object/submaterial overrides. Got {string.Join(',', first)} / {string.Join(',', second)}.");
    }

    private static void VerifyBakedModeTwoTintUsesFrozenConstant(string output)
    {
        const uint objectKey = 0x51000040;
        const uint materialKey = 0x47000040;
        const uint whiteTextureKey = 0x47000041;
        const uint blackTextureKey = 0x47000042;
        var resolver = new ResourceResolver();
        var geometry = new Geometry
        {
            Key = 0x47000043,
            Positions = [Vector3.Zero, new(.1f, 0, 0), new(0, 0, .1f)],
            Uvs = [new(.5f, .5f)],
            Parts = [new(0, [0, 1, 2], [0, 0, 0])]
        };
        Register(resolver, "_geometries", geometry.Key, geometry);
        RegisterTexture(resolver, whiteTextureKey, [255, 255, 255, 255]);
        RegisterTexture(resolver, blackTextureKey, [0, 0, 0, 255]);
        byte[] bytes = new byte[76];
        BitConverter.GetBytes(5).CopyTo(bytes, 0);
        BitConverter.GetBytes(0xffff0000u).CopyTo(bytes, 12); // source red; preview overrides it with green
        Layer(bytes, 36, 0, 0x00202004, whiteTextureKey);
        // Inverse/additive reflection has OverrideAlpha set; provide full local alpha
        // so the test isolates the base layer's mode-2 constant tint.
        Layer(bytes, 56, 0xf800, 0x00020484, blackTextureKey);
        Register(resolver, "_materials", materialKey,
            new ResolvedMaterial { Id = new(materialKey), Type = 5, SourceBytes = bytes });
        var world = new WorldData();
        world.Objects.Add(new SceneObject { GaoKey = objectKey, Name = "Baked constant", GeometryKey = geometry.Key,
            MaterialKey = materialKey });
        var snapshot = new CinematicMaterialLayerSnapshot(4, new Dictionary<(uint, int), JadeMaterialLayer>(),
            new Dictionary<(uint, int, int), byte>(),
            new Dictionary<(uint, int), Vector4> { [(objectKey, 0)] = new(0, 1, 0, 1) },
            new Dictionary<uint, float>());

        string path = Path.Combine(output, "cinematic-mode2-baked-constant.glb");
        SceneExporter.Export(path, world, resolver,
            new() { Textures = true, CinematicMaterialLayers = snapshot });
        using var glb = new GlbReader(path);
        using var stream = new MemoryStream(glb.Image(0));
        using var bitmap = new Bitmap(stream);
        Color pixel = bitmap.GetPixel(5, 5);
        Require(pixel.G > 220 && pixel.R < 8 && pixel.B < 8,
            $"Baked mode-2 reflection tint should use the frozen green constant instead of source red; got {pixel}.");
    }

    private static ResolvedMaterial ConstantMaterial(uint key, uint constant)
    {
        byte[] bytes = new byte[56];
        BitConverter.GetBytes(5).CopyTo(bytes, 0);
        BitConverter.GetBytes(constant).CopyTo(bytes, 12);
        BitConverter.GetBytes(0x00002000u).CopyTo(bytes, 40); // ColorMode 2
        return new() { Id = new(key), Type = 5, SourceBytes = bytes };
    }

    private static void Layer(byte[] bytes, int offset, ushort custom, uint flags, uint texture)
    {
        BitConverter.GetBytes(custom).CopyTo(bytes, offset + 2);
        BitConverter.GetBytes(flags).CopyTo(bytes, offset + 4);
        BitConverter.GetBytes(texture).CopyTo(bytes, offset + 16);
    }

    private static float[] MaterialColor(GlbReader glb, System.Text.Json.JsonElement primitive) =>
        glb.Root.GetProperty("materials")[primitive.GetProperty("material").GetInt32()]
            .GetProperty("pbrMetallicRoughness").GetProperty("baseColorFactor")
            .EnumerateArray().Select(value => value.GetSingle()).ToArray();

    private static bool Near(float[] actual, Vector4 expected) =>
        actual.Length == 4 && MathF.Abs(actual[0] - expected.X) < .0001f
            && MathF.Abs(actual[1] - expected.Y) < .0001f
            && MathF.Abs(actual[2] - expected.Z) < .0001f
            && MathF.Abs(actual[3] - expected.W) < .0001f;

    private static Vector4 PackedColor(uint packed) => new(
        (packed & 255) / 255f, (packed >> 8 & 255) / 255f,
        (packed >> 16 & 255) / 255f, (packed >> 24) / 255f);

    private static void RegisterTexture(ResourceResolver resolver, uint key, byte[] rgba)
    {
        var tga = new byte[18 + rgba.Length * 2];
        tga[2] = 2; tga[12] = 2; tga[14] = 1; tga[16] = 32; tga[17] = 0x28;
        for (int pixelIndex = 0; pixelIndex < 2; pixelIndex++)
        {
            int pixelOffset = 18 + pixelIndex * 4;
            tga[pixelOffset] = rgba[2];
            tga[pixelOffset + 1] = rgba[1];
            tga[pixelOffset + 2] = rgba[0];
            tga[pixelOffset + 3] = rgba[3];
        }
        Register(resolver, "_textures", key, new ResolvedTexture { Width = 2, Height = 1, TgaData = tga });
    }

    private static void Register<T>(ResourceResolver resolver, string field, uint key, T value) =>
        ((Dictionary<uint, T>)typeof(ResourceResolver).GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(resolver)!).Add(key, value);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
