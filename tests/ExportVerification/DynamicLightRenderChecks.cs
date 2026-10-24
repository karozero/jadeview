using System.Numerics;
using System.Reflection;
using JadeView;
using Raylib_cs;

internal static class DynamicLightRenderChecks
{
    internal static void Verify()
    {
        const uint receiverKey = 0x901;
        const uint lightKey = 0x902;
        const uint geometryKey = 0x903;
        const uint computedMaterialKey = 0x904;
        const uint rawMaterialKey = 0x905;
        const uint textureKey = 0x906;
        const uint active = 0x00000008;
        const uint useColor = 0x00000020;
        const uint staticReceiver = 0x00000200;
        const uint lightedDrawFlag = 0x08000000;
        const uint useRliDrawFlag = 0x00000040;
        const uint visibleDrawFlags = 0x01000008;

        var resolver = new ResourceResolver();
        Register(resolver, "_geometries", geometryKey, CreateGeometry());
        Register(resolver, "_materials", computedMaterialKey, CreateMaterial(computedMaterialKey, textureKey, 0));
        Register(resolver, "_materials", rawMaterialKey, CreateMaterial(rawMaterialKey, textureKey, 3));
        RegisterTexture(resolver, textureKey);

        var receiver = new SceneObject
        {
            GaoKey = receiverKey,
            Name = "Dynamic-light receiver",
            GeometryKey = geometryKey,
            MaterialKey = computedMaterialKey,
            Flags = 0x00004000,
            DrawFlags = visibleDrawFlags | lightedDrawFlag | useRliDrawFlag,
            RliColors = []
        };
        var lightOwner = new SceneObject
        {
            GaoKey = lightKey,
            Name = "Direct light owner",
            Flags = 0x10,
            LightResourceKey = 0x907,
            Transform = new()
            {
                Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI)
            },
            Light = new SceneLight(active | useColor | staticReceiver | (uint)SceneLightType.Direct,
                0xff0000ff, 0, 100, 0, 0, SceneLight.NoLightGaoKey)
        };
        var world = new WorldData { Name = "dynamic light GPU check" };
        world.Objects.Add(receiver);
        world.Objects.Add(lightOwner);

        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(160, 120, "Dynamic light verification");
        try
        {
            using var renderer = new SceneRenderer
            {
                ShowGrid = false,
                ShowGizmo = false,
                GameView = true,
                FrustumCullingEnabled = false
            };
            renderer.Camera.Position = new(0, 0, 5);
            renderer.Camera.Target = Vector3.Zero;
            renderer.Camera.Up = Vector3.UnitY;

            Raylib_cs.Color rawMode = RenderPixel(renderer, world, resolver, receiver, rawMaterialKey);
            AssertPixelNear(rawMode, new(96, 64, 32, 255), 5,
                "Mode 3 should display the authored raw RLI even when a dynamic light is present.");

            receiver.MaterialKey = computedMaterialKey;
            Raylib_cs.Color directLit = RenderPixel(renderer, world, resolver, receiver, computedMaterialKey);
            AssertPixelNear(directLit, new(255, 64, 32, 255), 6,
                "An active direct light should add its selected red diffuse contribution to computed mode 0.");
            var cached = RenderPixel(renderer, world, resolver, receiver, computedMaterialKey);
            AssertPixelNear(cached, directLit, 1, "Static cached lighting changed its rendered color.");
            if (renderer.StatDiffuseLightingUpdates != 0 || renderer.StatDiffuseLightingPreparations != 0)
                throw new InvalidDataException("Unchanged static lighting must reuse both its prepared plan and GPU colors.");
            renderer.DynamicLighting = false;
            AssertPixelNear(RenderPixel(renderer, world, resolver, receiver, computedMaterialKey), rawMode, 5,
                "Disabling dynamic lighting must preserve the existing RLI preview.");
            renderer.DynamicLighting = true;
            AssertPixelNear(RenderPixel(renderer, world, resolver, receiver, computedMaterialKey), directLit, 5,
                "Re-enabling dynamic lighting must restore the cached diffuse preview.");

            VerifySharedGeometryInstances(renderer, world, resolver, receiver, computedMaterialKey);
            VerifyLightedMask(renderer, world, resolver, receiver, computedMaterialKey, rawMode,
                visibleDrawFlags, lightedDrawFlag, useRliDrawFlag);
            VerifyInactiveLight(renderer, world, resolver, receiver, lightOwner, computedMaterialKey,
                rawMode, active, useColor, staticReceiver);
            VerifyPlayheadLightFlags(renderer, world, resolver, receiver, lightOwner,
                computedMaterialKey, rawMode, directLit, active, useColor, staticReceiver);
            VerifyOmniRangeAndOwnerFlag(renderer, world, resolver, receiver, lightOwner,
                computedMaterialKey, rawMode, active, useColor, staticReceiver);

            VerifyUniformCacheImageEquality(renderer, world, resolver, receiver, computedMaterialKey, rawMaterialKey);
            Console.WriteLine("PASS: GPU dynamic diffuse color, raw-mode isolation, light masks, playhead state restoration, and omni range");
        }
        finally
        {
            Raylib.CloseWindow();
        }
    }

    private static unsafe void VerifyUniformCacheImageEquality(SceneRenderer renderer, WorldData world,
        ResourceResolver resolver, SceneObject receiver, uint computedMaterialKey, uint rawMaterialKey)
    {
        foreach (uint materialKey in new[] { computedMaterialKey, rawMaterialKey, computedMaterialKey })
        {
            receiver.MaterialKey = materialKey;
            renderer.CacheShaderUniformValues = false;
            byte[] reference = Capture();
            renderer.CacheShaderUniformValues = true;
            byte[] cached = Capture();
            if (!reference.AsSpan().SequenceEqual(cached))
                throw new InvalidDataException("Uniform caching changed the rendered RGBA image.");
            if (renderer.StatShaderUniformUploads >= renderer.StatShaderUniformRequests)
                throw new InvalidDataException("An identical static frame should reuse shader uniform values.");
        }
        Console.WriteLine("PASS: cached/uncached shader uniforms produce bit-identical complete GPU images across material switches");

        byte[] Capture()
        {
            renderer.Render(160, 120, new(), world, null, resolver);
            var image = Raylib.LoadImageFromTexture(renderer.Target.Texture);
            var colors = Raylib.LoadImageColors(image);
            try { return new ReadOnlySpan<byte>(colors, image.Width * image.Height * 4).ToArray(); }
            finally
            {
                Raylib.UnloadImageColors(colors);
                Raylib.UnloadImage(image);
            }
        }
    }
    private static void VerifySharedGeometryInstances(SceneRenderer renderer, WorldData world,
        ResourceResolver resolver, SceneObject receiver, uint materialKey)
    {
        var other = new SceneObject
        {
            GaoKey = 0x908,
            GeometryKey = receiver.GeometryKey,
            MaterialKey = materialKey,
            Flags = receiver.Flags,
            DrawFlags = receiver.DrawFlags,
            RliColors = [0xff00ff00, 0xff00ff00, 0xff00ff00]
        };
        world.Objects.Add(other);
        try
        {
            var greenInstance = RenderPixel(renderer, world, resolver, receiver, materialKey);
            AssertPixelNear(greenInstance, new(255, 255, 0, 255), 6,
                "A shared-GRO instance must apply its own RLI instead of the previous owner's sample.");
            AssertPixelNear(RenderPixel(renderer, world, resolver, receiver, materialKey), greenInstance, 1,
                "Cached shared-GRO instance colors changed between frames.");
            if (renderer.StatDiffuseLightingUpdates != 0)
                throw new InvalidDataException("Both static shared-GRO instances should reuse CPU lighting samples.");
            world.Objects.Remove(receiver);
            world.Objects.Add(receiver);
            AssertPixelNear(RenderPixel(renderer, world, resolver, receiver, materialKey), new(255, 64, 32, 255), 6,
                "Reordering shared-GRO instances must upload the original instance's cached colors.");
            other.RliColors = [0xffff0000, 0xffff0000, 0xffff0000];
            other.AppearanceRevision++;
            world.Objects.Remove(other);
            world.Objects.Add(other);
            AssertPixelNear(RenderPixel(renderer, world, resolver, receiver, materialKey), new(255, 0, 255, 255), 6,
                "Changing an instance's appearance must invalidate only its cached computed colors.");
        }
        finally
        {
            world.Objects.Remove(other);
        }
    }
    private static void VerifyLightedMask(SceneRenderer renderer, WorldData world, ResourceResolver resolver,
        SceneObject receiver, uint computedMaterialKey, Raylib_cs.Color rawMode,
        uint visibleDrawFlags, uint lightedDrawFlag, uint useRliDrawFlag)
    {
        receiver.DrawFlags = visibleDrawFlags | useRliDrawFlag;
        var unlighted = RenderPixel(renderer, world, resolver, receiver, computedMaterialKey);
        AssertPixelNear(unlighted, rawMode, 5,
            "Clearing the Lighted draw-mask must exclude dynamic diffuse light from the receiver.");
        receiver.DrawFlags |= lightedDrawFlag;
    }

    private static void VerifyInactiveLight(SceneRenderer renderer, WorldData world, ResourceResolver resolver,
        SceneObject receiver, SceneObject lightOwner, uint computedMaterialKey, Raylib_cs.Color rawMode,
        uint active, uint useColor, uint staticReceiver)
    {
        SceneLight original = lightOwner.Light!;
        lightOwner.Light = original with { Flags = original.Flags & ~active };
        var inactive = RenderPixel(renderer, world, resolver, receiver, computedMaterialKey);
        AssertPixelNear(inactive, rawMode, 5,
            "An inactive source light must not contribute to the rendered vertex color.");
        lightOwner.Light = CreateDirectLight(active, useColor, staticReceiver);
    }

    private static void VerifyPlayheadLightFlags(SceneRenderer renderer, WorldData world, ResourceResolver resolver,
        SceneObject receiver, SceneObject lightOwner, uint computedMaterialKey, Raylib_cs.Color rawMode,
        Raylib_cs.Color directLit, uint active, uint useColor, uint staticReceiver)
    {
        lightOwner.Light = CreateDirectLight(active, useColor, staticReceiver);
        var timeline = new CinematicLightStateTimeline(world.Objects,
        [
            (1f, new CinematicLiteralCall(lightOwner.GaoKey, 0x213B, [3, 0])),
            (2f, new CinematicLiteralCall(lightOwner.GaoKey, 0x213B, [3, 1]))
        ]);

        renderer.CinematicLights = timeline.Evaluate(.5f);
        var initial = RenderPixel(renderer, world, resolver, receiver, computedMaterialKey);
        AssertPixelNear(initial, directLit, 5,
            "The baseline playhead should use the source light component unchanged.");

        renderer.CinematicLights = timeline.Evaluate(1.5f);
        var disabled = RenderPixel(renderer, world, resolver, receiver, computedMaterialKey);
        AssertPixelNear(disabled, rawMode, 5,
            "A cinematic LightSetFlag-off event must remove its diffuse contribution at that playhead.");

        renderer.CinematicLights = timeline.Evaluate(2.5f);
        var restored = RenderPixel(renderer, world, resolver, receiver, computedMaterialKey);
        AssertPixelNear(restored, directLit, 5,
            "Seeking back to LightSetFlag-on must refresh cached computed vertex colors.");
        var sameLights = renderer.CinematicLights!;
        renderer.CinematicLights = sameLights with { Lights = sameLights.Lights.ToArray() };
        var equivalent = RenderPixel(renderer, world, resolver, receiver, computedMaterialKey);
        AssertPixelNear(equivalent, restored, 1, "Equivalent sampled lights changed the rendered color.");
        if (renderer.StatDiffuseLightingUpdates != 0 || renderer.StatDiffuseLightingPreparations != 0)
            throw new InvalidDataException("A new snapshot with unchanged effective lights must preserve prepared plans and GPU colors.");
        renderer.CinematicLights = null;
    }

    private static void VerifyOmniRangeAndOwnerFlag(SceneRenderer renderer, WorldData world,
        ResourceResolver resolver, SceneObject receiver, SceneObject lightOwner, uint computedMaterialKey,
        Raylib_cs.Color rawMode, uint active, uint useColor, uint staticReceiver)
    {
        receiver.DrawFlags |= 0x08000000;
        lightOwner.Transform = new() { Translation = new(0, 0, 10) };
        lightOwner.Light = new SceneLight(active | useColor | staticReceiver | 0x10u | (uint)SceneLightType.Omni,
            0xff0000ff, 0, 100, 0, 0, SceneLight.NoLightGaoKey);

        Raylib_cs.Color inRange = RenderPixel(renderer, world, resolver, receiver, computedMaterialKey);
        if (inRange.R < rawMode.R + 80 || Math.Abs(inRange.G - rawMode.G) > 6 || Math.Abs(inRange.B - rawMode.B) > 6)
            throw new InvalidDataException($"The active in-range omni light did not add red diffuse: raw={rawMode}, lit={inRange}.");

        lightOwner.Light = lightOwner.Light! with { Far = 3 };
        Raylib_cs.Color outOfRange = RenderPixel(renderer, world, resolver, receiver, computedMaterialKey);
        AssertPixelNear(outOfRange, rawMode, 5,
            "An omni light beyond its far range must not contribute to the receiver.");

        lightOwner.Flags &= ~0x10u;
        lightOwner.Light = lightOwner.Light! with { Far = 100 };
        Raylib_cs.Color ownerNotLight = RenderPixel(renderer, world, resolver, receiver, computedMaterialKey);
        AssertPixelNear(ownerNotLight, rawMode, 5,
            "A light component without the light-owner identity flag must not enter the world light list.");
    }

    private static SceneLight CreateDirectLight(uint active, uint useColor, uint staticReceiver) =>
        new(active | useColor | staticReceiver | (uint)SceneLightType.Direct,
            0xff0000ff, 0, 100, 0, 0, SceneLight.NoLightGaoKey);

    private static Geometry CreateGeometry() => new()
    {
        Key = 0x903,
        // This triangle's source-space normal points along -Y. The direct light rotates
        // its local +Y axis by pi around Z so the source dot-product is exactly positive.
        Positions = [new(-2, 0, -2), new(2, 0, -2), new(0, 0, 2)],
        VertexColors = [0xff204060, 0xff204060, 0xff204060],
        Parts = [new(0, [0, 1, 2], [])]
    };

    private static ResolvedMaterial CreateMaterial(uint materialKey, uint textureKey, int colorMode)
    {
        byte[] bytes = new byte[56];
        BitConverter.GetBytes(5).CopyTo(bytes, 0);
        BitConverter.GetBytes((uint)colorMode << 12 | 0x00000007).CopyTo(bytes, 40);
        BitConverter.GetBytes(textureKey).CopyTo(bytes, 52);
        return new ResolvedMaterial { Id = new(materialKey), Type = 5, SourceBytes = bytes };
    }

    private static Raylib_cs.Color RenderPixel(SceneRenderer renderer, WorldData world,
        ResourceResolver resolver, SceneObject receiver, uint materialKey)
    {
        receiver.MaterialKey = materialKey;
        renderer.Render(160, 120, new(), world, null, resolver);
        var image = Raylib.LoadImageFromTexture(renderer.Target.Texture);
        try { return Raylib.GetImageColor(image, 80, 60); }
        finally { Raylib.UnloadImage(image); }
    }

    private static void RegisterTexture(ResourceResolver resolver, uint key)
    {
        byte[] tga = new byte[22];
        tga[2] = 2;
        tga[12] = 1;
        tga[14] = 1;
        tga[16] = 32;
        tga[17] = 0x28;
        tga[18] = 255;
        tga[19] = 255;
        tga[20] = 255;
        tga[21] = 255;
        Register(resolver, "_textures", key,
            new ResolvedTexture { Width = 1, Height = 1, TgaData = tga });
    }

    private static void Register<T>(ResourceResolver resolver, string field, uint key, T value) =>
        ((Dictionary<uint, T>)typeof(ResourceResolver).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(resolver)!).Add(key, value);

    private static void AssertPixelNear(Raylib_cs.Color actual, Raylib_cs.Color expected,
        int tolerance, string message)
    {
        if (Math.Abs(actual.R - expected.R) > tolerance || Math.Abs(actual.G - expected.G) > tolerance
            || Math.Abs(actual.B - expected.B) > tolerance || Math.Abs(actual.A - expected.A) > tolerance)
            throw new InvalidDataException($"{message}: expected {expected} ±{tolerance}, observed {actual}.");
    }
}
