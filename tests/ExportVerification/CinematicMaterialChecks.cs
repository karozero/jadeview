using System.Numerics;
using JadeView;

internal static class CinematicMaterialChecks
{
    internal static void Verify()
    {
        const uint materialKey = 0x47000001, rootKey = 0x47000002, secondMaterialKey = 0x47000003;
        byte[] bytes = new byte[76];
        byte[] secondMaterialBytes = new byte[76];
        BitConverter.GetBytes(5).CopyTo(bytes, 0);
        BitConverter.GetBytes(5).CopyTo(secondMaterialBytes, 0);
        for (int level = 0; level < 2; level++)
        {
            BitConverter.GetBytes((ushort)0xf860).CopyTo(bytes, 38 + level * 20);
            BitConverter.GetBytes(0x00010c00u).CopyTo(bytes, 40 + level * 20);
            BitConverter.GetBytes((ushort)0xf860).CopyTo(secondMaterialBytes, 38 + level * 20);
            BitConverter.GetBytes(0x00010c00u).CopyTo(secondMaterialBytes, 40 + level * 20);
        }
        var materials = new Dictionary<uint, ResolvedMaterial>
        {
            [materialKey] = new() { Id = new(materialKey), Type = 5, SourceBytes = bytes },
            [secondMaterialKey] = new() { Id = new(secondMaterialKey), Type = 5, SourceBytes = secondMaterialBytes },
            [rootKey] = new() { Id = new(rootKey), Type = 4 }
        };
        materials[rootKey].SubMaterialKeys.Add(materialKey);
        materials[rootKey].SubMaterialKeys.Add(secondMaterialKey);
        var world = new WorldData();
        world.Objects.Add(new() { GaoKey = 7, MaterialKey = rootKey });
        var clip = new AnimationClip
        {
            Tracks = [new() { Target = 7, Bone = -1, Keys =
            [CinematicTestEvents.Call(1, 7, 15006, materialKey, 1, 1),
             CinematicTestEvents.Call(2, 7, 15008, 2, 1, 101), // Native modulo submaterial lookup.
             CinematicTestEvents.Call(3, 7, 15010, materialKey, 1, 255),
             CinematicTestEvents.Call(4, 7, 15006, materialKey, 1, 0),
             CinematicTestEvents.Call(5, 7, 15013, rootKey, 1, 1, 1),
             CinematicTestEvents.Call(6, 7, 15013, rootKey, 1, 1, 0),
             CinematicTestEvents.Call(7, 7, 15000, 0xff112233, 1),
             CinematicTestEvents.Call(8, 7, 15000, 0xffaabbcc, materialKey)] }]
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
        var secondOriginal = JadeMaterialLayer.Read(secondMaterialBytes)[1];
        state.Evaluate(5.5f);
        Require(!state.Apply(secondMaterialKey, 1, secondOriginal).Disabled
            && state.Apply(materialKey, 1, original).Disabled,
            "MATEnableMltxtLvlMT must resolve its explicit submaterial and enable only the selected texture level.");
        state.Evaluate(6.5f);
        Require(state.Apply(secondMaterialKey, 1, secondOriginal).Disabled,
            "MATEnableMltxtLvlMT with enable=0 must set the native inactive bit on the selected level.");
        state.Evaluate(7.5f);
        Require(state.ApplyConstant(secondMaterialKey, 7, 1, Vector4.One) == PackedColor(0xff112233)
            && state.ApplyConstant(materialKey, 7, 0, Vector4.One) == Vector4.One,
            "MATSetConstant must resolve a low selector through the target object's multi-material without changing siblings.");
        state.Evaluate(8.5f);
        Require(state.ApplyConstant(materialKey, 7, 0, Vector4.One) == PackedColor(0xffaabbcc)
            && state.ApplyConstant(secondMaterialKey, 7, 1, Vector4.One) == PackedColor(0xff112233),
            "MATSetConstant must resolve a global material key and retain independent material overrides.");
        state.Evaluate(5.5f);
        Require(!state.Apply(secondMaterialKey, 1, secondOriginal).Disabled,
            "Rewinding must remove MATEnableMltxtLvlMT changes after the playhead.");
        Require(state.ApplyConstant(materialKey, 7, 0, Vector4.One) == Vector4.One
            && state.ApplyConstant(secondMaterialKey, 7, 1, Vector4.One) == Vector4.One,
            "Rewinding must remove later script-driven constant-color changes.");
        state.Evaluate(.5f);
        Require(state.Apply(materialKey, 1, original) == original && JadeMaterialLayer.Read(bytes)[1] == original,
            "Material reverse seek or immutable source state was lost.");
        Require(state.Time == .5f, "Cinematic material time must rewind with a reverse seek.");
        VerifyNestedMaterialCommands(world, materials, materialKey, original);
        VerifyWorldCycleSampling();
        VerifyWorldLightingHeader();
        VerifySimpleMaterialOpacity();
        VerifySpriteAlpha();
        VerifyRliAlphaPolicy();
        Console.WriteLine("PASS: timed material activation/constant colors, multi-material routing, packed alpha/threshold, layer isolation and reverse seek");
    }

    private static void VerifyRliAlphaPolicy()
    {
        static JadeMaterialLayer Layer(int blend, uint flags = 0) =>
            new(0, ((uint)blend << 16) | flags, 0, 0, 0);

        Require(MaterialRliAlphaPolicy.UsesRliAlpha(Layer(1), null)
            && MaterialRliAlphaPolicy.UsesRliAlpha(Layer(2), null),
            "Alpha and premultiplied-alpha layers must consume RLI alpha when local alpha is not authored.");
        Require(!MaterialRliAlphaPolicy.UsesRliAlpha(Layer(1, JadeMaterialFlags.OverrideAlpha), null)
            && !MaterialRliAlphaPolicy.UsesRliAlpha(Layer(5), null),
            "Local-alpha and additive layers must not consume RLI alpha as source transparency.");
        Require(MaterialRliAlphaPolicy.UsesRliAlpha(Layer(0), Layer(3))
            && MaterialRliAlphaPolicy.UsesRliAlpha(Layer(7), Layer(4))
            && !MaterialRliAlphaPolicy.UsesRliAlpha(Layer(0), Layer(1)),
            "Copy/glow layers consume RLI alpha only when the following pass uses destination alpha.");
        Require(MaterialRliAlphaPolicy.UsesRliAlpha(Layer(5, JadeMaterialFlags.AlphaTest), null),
            "An alpha-tested layer must consume RLI alpha even when its color blend is additive.");
    }

    private static void VerifyNestedMaterialCommands(WorldData world,
        IReadOnlyDictionary<uint, ResolvedMaterial> materials, uint materialKey,
        JadeMaterialLayer original)
    {
        // A scheduled child EVE can change material state without any command
        // authored directly on the selected cinematic clip.
        (float Time, CinematicLiteralCall Call)[] nestedCommands =
        [
            (2, new(7, 15006, [materialKey, 1, 1])),
            (3, new(7, 15008, [materialKey, 1, 64])),
            (4, new(7, 15006, [materialKey, 1, 0]))
        ];
        var state = new CinematicMaterialState(new AnimationClip(), world, materials,
            [], nestedCommands);
        state.Evaluate(3.5f);
        var evaluated = state.Apply(materialKey, 1, original);
        Require(!evaluated.Disabled && evaluated.Custom >> 11 == 8,
            "Scheduled child-EVE material enable/local-alpha commands were ignored.");
        Require(state.Apply(materialKey, 0, original) == original,
            "A child-EVE command changed an unrelated material layer.");
        state.Evaluate(4);
        Require(state.Apply(materialKey, 1, original).Disabled,
            "Scheduled child-EVE material disable was ignored.");
        state.Evaluate(1);
        Require(state.Apply(materialKey, 1, original) == original,
            "Rewinding must remove child-EVE material state without mutating source layers.");
    }

    private static void VerifyWorldCycleSampling()
    {
        const uint sunKey = 0x47000010;
        var sun = new SceneObject { GaoKey = sunKey, Transform = new() { Translation = new(0, 0, -.5f) } };
        var world = new WorldData();
        world.Objects.Add(sun);
        var owner = new SceneObject { GaoKey = 1 };
        world.Objects.Add(owner);
        const uint moonKey = 0x47000020;
        world.Objects.Add(new SceneObject { GaoKey = moonKey });
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
            MoonKey = moonKey,
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
            && constants[(9, 0)] == PackedColor(palettes[0][4])
            && constants[(moonKey, 0)] == PackedColor(palettes[0][6]),
            "World-cycle palette endpoint colors or scene bindings changed.");
        Require(state.RliColor == PackedColor(palettes[0][7]) && state.RliFactor == .1f
            && state.FogColor == PackedColor(palettes[0][1]) && state.FogNear == 10 && state.FogFar == 100
            && alphas[(8, 0, 1)] == 127,
            "Night environment, fog, or local-alpha state changed during allocation-free sampling.");
        Require(state.HasAmbient && state.AmbientColor == PackedColor(palettes[0][0])
            && state.AmbientColor2 == PackedColor(palettes[0][5]),
            "World-cycle ambient channels must use palette entries 0 and 5 independently.");
        sun.PreviewMatrix = Matrix4x4.CreateTranslation(0, 0, 4);
        state = CinematicWorldCyclePalette.Evaluate(controllers, world, constants, alphas);
        Require(state.AmbientColor == PackedColor(palettes[3][0]) && state.AmbientColor2 == PackedColor(palettes[3][5]),
            "Animated sun height must update both world ambient channels.");
        sun.PreviewMatrix = null;
        state = CinematicWorldCyclePalette.Evaluate(controllers, world, constants, alphas);
        Require(state.AmbientColor == PackedColor(palettes[0][0]), "Seeking sun height backward must restore ambient.");
        var attackPalettes = palettes.Select(palette => palette.Select(color => color ^ 0x00003040u).ToArray()).ToArray();
        state = CinematicWorldCyclePalette.Evaluate([controller with { AttackPalettes = attackPalettes }],
            world, constants, alphas, 1);
        Require(state.AmbientColor == PackedColor(attackPalettes[0][0])
            && state.AmbientColor2 == PackedColor(attackPalettes[0][5])
            && constants[(moonKey, 0)] == PackedColor(attackPalettes[0][6]), "Attack palettes must affect ambient and the moon material.");
        owner.PreviewCapabilities = 256;
        state = CinematicWorldCyclePalette.Evaluate(controllers, world, constants, alphas);
        Require(!state.HasAmbient && state.AmbientColor == Vector4.Zero && state.AmbientColor2 == Vector4.Zero,
            "Capability 256 must leave ambient unresolved rather than reuse the previous evaluation.");
        owner.PreviewCapabilities = null;

        for (int iteration = 0; iteration < 32; iteration++)
            _ = CinematicWorldCyclePalette.Evaluate(controllers, world, constants, alphas);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 1024; iteration++)
            _ = CinematicWorldCyclePalette.Evaluate(controllers, world, constants, alphas);
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Require(allocatedBytes == 0,
            $"World-cycle preview allocated {allocatedBytes} bytes for 1024 cached palette evaluations.");
        world.Objects.Add(new SceneObject { GaoKey = moonKey });
        _ = CinematicWorldCyclePalette.Evaluate(controllers, world, constants, alphas);
        Require(!constants.ContainsKey((moonKey, 0)), "Ambiguous moon targets must not receive an arbitrary material override.");
        world.Objects.Add(new SceneObject { GaoKey = sunKey });
        _ = CinematicWorldCyclePalette.Evaluate(controllers, world, constants, alphas);
        Require(constants.Count == 0, "An ambiguous sun cannot select a world-cycle palette by list order.");
    }

    private static Vector4 PackedColor(uint color) => new(
        (color & 0xff) / 255f,
        (color >> 8 & 0xff) / 255f,
        (color >> 16 & 0xff) / 255f,
        (color >> 24) / 255f);

    private static void VerifyWorldLightingHeader()
    {
        byte[] payload = new byte[180];
        ".wow"u8.CopyTo(payload);
        BitConverter.GetBytes(3).CopyTo(payload, 4);
        BitConverter.GetBytes(0xff102030u).CopyTo(payload, 12);
        BitConverter.GetBytes(uint.MaxValue).CopyTo(payload, 152);
        var lighting = PcWorldLightingReader.Read(payload);
        Require(lighting is { AmbientColor: 0xff102030, EffectiveAmbientColor2: 0xff102030 },
            "PC WOW ambient colors or the native -1 second-channel fallback were not retained.");
        Require(PcWorldLightingReader.Read(payload.AsSpan(0, 155)) == null,
            "Truncated WOW headers must not invent world lighting.");
        payload[4] = 4;
        Require(PcWorldLightingReader.Read(payload) == null, "Unknown WOW versions must not use v3 offsets.");
        var world = new WorldData { Lighting = lighting };
        var constants = new Dictionary<(uint, int), Vector4>();
        var alphas = new Dictionary<(uint, int, int), byte>();
        var state = CinematicWorldCyclePalette.Evaluate([], world, constants, alphas);
        Require(state.HasAmbient && state.AmbientColor == PackedColor(0xff102030)
            && state.AmbientColor2 == state.AmbientColor,
            "Scenes without world-cycle scripts must keep serialized world ambient.");
        var document = new SceneDocument();
        var cinematic = new WorldData { Key = 1, Name = "Test_cine", Lighting = lighting };
        document.Worlds.Add(cinematic);
        Require(CinematicWorldAssembler.Assemble(document, cinematic).Lighting == lighting,
            "Assembling a cinematic must preserve its serialized world lighting.");
        var environmentLighting = new WorldLighting(0xff305070, 0xff406080) { SourceWorldKey = 2 };
        document.Worlds.Add(new WorldData { Key = 2, Name = "Test", Lighting = environmentLighting });
        Require(CinematicWorldAssembler.Assemble(document, cinematic).Lighting == environmentLighting,
            "Merged world ambient must follow the incoming WOW order rather than a cinematic definition header.");
    }

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
