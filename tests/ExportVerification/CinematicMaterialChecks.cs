using System.Numerics;
using JadeView;

internal static class CinematicMaterialChecks
{
    internal static void Verify()
    {
        const uint materialKey = 0x47000001, rootKey = 0x47000002;
        byte[] bytes = new byte[76];
        BitConverter.GetBytes(5).CopyTo(bytes, 0);
        for (int level = 0; level < 2; level++)
        {
            BitConverter.GetBytes((ushort)0xf860).CopyTo(bytes, 38 + level * 20);
            BitConverter.GetBytes(0x00010c00u).CopyTo(bytes, 40 + level * 20);
        }
        var materials = new Dictionary<uint, ResolvedMaterial>
        {
            [materialKey] = new() { Id = new(materialKey), Type = 5, SourceBytes = bytes },
            [rootKey] = new() { Id = new(rootKey), Type = 4 }
        };
        materials[rootKey].SubMaterialKeys.Add(materialKey);
        var world = new WorldData();
        world.Objects.Add(new() { GaoKey = 7, MaterialKey = rootKey });
        var clip = new AnimationClip
        {
            Tracks = [new() { Target = 7, Bone = -1, Keys =
            [CinematicTestEvents.Call(1, 7, 15006, materialKey, 1, 1),
             CinematicTestEvents.Call(2, 7, 15008, 3, 1, 101), // Native modulo submaterial lookup.
             CinematicTestEvents.Call(3, 7, 15010, materialKey, 1, 255),
             CinematicTestEvents.Call(4, 7, 15006, materialKey, 1, 0)] }]
        };
        var state = new CinematicMaterialState(clip, world, materials);
        var original = JadeMaterialLayer.Read(bytes)[1];
        state.Evaluate(3.5f);
        Require(state.Time == 3.5f, "Cinematic material time must follow the playhead.");
        var evaluated = state.Apply(materialKey, 1, original);
        Require(!evaluated.Disabled && evaluated.Custom == 0x6060 && (evaluated.Flags >> 22 & 0xfc) == 252,
            "Native material enable, five-bit local alpha or six-bit threshold was lost.");
        Require(state.Apply(materialKey, 0, original) == original, "A command changed an unrelated layer.");
        state.Evaluate(4.5f);
        Require(state.Apply(materialKey, 1, original).Disabled, "Material disable event was ignored.");
        state.Evaluate(.5f);
        Require(state.Apply(materialKey, 1, original) == original && JadeMaterialLayer.Read(bytes)[1] == original,
            "Material reverse seek or immutable source state was lost.");
        Require(state.Time == .5f, "Cinematic material time must rewind with a reverse seek.");
        VerifyWorldCycleSampling();
        VerifySimpleMaterialOpacity();
        VerifySpriteAlpha();
        Console.WriteLine("PASS: timed material activation, native key/index lookup, packed alpha/threshold, layer isolation and reverse seek");
    }

    private static void VerifyWorldCycleSampling()
    {
        const uint sunKey = 0x47000010;
        var sun = new SceneObject { GaoKey = sunKey, Transform = new() { Translation = new(0, 0, -.5f) } };
        var world = new WorldData();
        world.Objects.Add(sun);
        uint[][] palettes = Enumerable.Range(0, 4)
            .Select(paletteIndex => Enumerable.Range(0, 9)
                .Select(colorIndex => 0xff000000u | (uint)(paletteIndex * 24) << 16
                    | (uint)(paletteIndex * 24) << 8 | (uint)(paletteIndex * 24 + colorIndex)).ToArray())
            .ToArray();
        var controller = new CinematicEnvironmentController(1, 0, [sunKey])
        {
            EnvironmentKey = 8,
            SkyKey = 9,
            SunKey = sunKey,
            NightHeight = 0,
            DawnHeight = 1,
            SunriseHeight = 2,
            DayHeight = 3,
            NightRliFactor = .1f,
            DawnRliFactor = .2f,
            SunsetRliFactor = .3f,
            DayRliFactor = .4f,
            FogNear = 10,
            FogFar = 100,
            AttackFogNear = 20,
            AttackFogFar = 200,
            NormalPalettes = palettes
        };
        CinematicEnvironmentController[] controllers = [controller];
        var constants = new Dictionary<(uint ObjectKey, int SubMaterial), Vector4>();
        var alphas = new Dictionary<(uint ObjectKey, int SubMaterial, int Layer), byte>();
        var state = CinematicWorldCyclePalette.Evaluate(controllers, world, constants, alphas);
        Require(constants[(8, 0)] == PackedColor(palettes[0][3])
            && constants[(8, 1)] == PackedColor(palettes[0][8])
            && constants[(9, 0)] == PackedColor(palettes[0][4]),
            "World-cycle palette endpoint colors or scene bindings changed.");
        Require(state.RliColor == PackedColor(palettes[0][7]) && state.RliFactor == .1f
            && state.FogColor == PackedColor(palettes[0][1]) && state.FogNear == 10 && state.FogFar == 100
            && alphas[(8, 0, 1)] == 127,
            "Night environment, fog, or local-alpha state changed during allocation-free sampling.");

        for (int iteration = 0; iteration < 32; iteration++)
            _ = CinematicWorldCyclePalette.Evaluate(controllers, world, constants, alphas);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 1024; iteration++)
            _ = CinematicWorldCyclePalette.Evaluate(controllers, world, constants, alphas);
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Require(allocatedBytes == 0,
            $"World-cycle preview allocated {allocatedBytes} bytes for 1024 cached palette evaluations.");
    }

    private static Vector4 PackedColor(uint color) => new(
        (color & 0xff) / 255f,
        (color >> 8 & 0xff) / 255f,
        (color >> 16 & 0xff) / 255f,
        (color >> 24) / 255f);

    private static void VerifySimpleMaterialOpacity()
    {
        var bytes = new byte[36];
        BitConverter.GetBytes(3).CopyTo(bytes, 0);
        BitConverter.GetBytes(.5f).CopyTo(bytes, 20);
        BitConverter.GetBytes(0x12345678u).CopyTo(bytes, 28);
        var translucent = JadeMaterialLayer.Read(bytes).Single();
        Require(Math.Abs(translucent.Opacity - .5f) < .0001f && translucent.Blend == 1
            && translucent.Texture == 0x12345678,
            "Simple material opacity must enable alpha blending and preserve its texture key.");

        BitConverter.GetBytes(1f).CopyTo(bytes, 20);
        var opaque = JadeMaterialLayer.Read(bytes).Single();
        Require(opaque.Opacity == 1 && opaque.Blend == 0,
            "Opaque simple materials must retain copy blending.");
    }

    private static void VerifySpriteAlpha()
    {
        var owner = new SceneObject { RliColors = [0x20ffffff] };
        var geometry = new Geometry { VertexColors = [0x20ffffff, 0x40ffffff, 0x40ffffff, 0x40ffffff, 0x40ffffff] };
        geometry.Billboards.Add(new(1, default, 1, 1, false));
        var layer = new JadeMaterialLayer(0, 0x10000, 0, 0, 0);
        if (Math.Abs(GeometryVertexColor.InstanceAlpha(owner, geometry, 1, 0x40ffffff) - 64 / 255f) > .0001f
            || Math.Abs(MaterialLayerTint.Evaluate(owner, geometry, 1, null, layer).W - 64 / 255f) > .0001f
            || GeometryVertexColor.InstanceAlpha(owner, new(), 5, 0) != 1)
            throw new InvalidDataException("Generated SPG alpha was replaced by opaque carrier fallback or diverged during export.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
