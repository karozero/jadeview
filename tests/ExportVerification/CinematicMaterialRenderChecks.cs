using System.Numerics;
using System.Reflection;
using JadeView;
using Raylib_cs;

internal static class CinematicMaterialRenderChecks
{
    internal static void Verify()
    {
        const uint geometryKey = 100, materialKey = 0x47000001;
        var geometry = new Geometry
        {
            Key = geometryKey,
            Positions = [new(-2, 0, -2), new(2, 0, -2), new(0, 0, 2)],
            Parts = [new(0, [0, 1, 2], [])]
        };
        var bytes = new byte[56];
        BitConverter.GetBytes(5).CopyTo(bytes, 0);
        BitConverter.GetBytes((ushort)0xf800).CopyTo(bytes, 38);
        BitConverter.GetBytes(0x00014c07u).CopyTo(bytes, 40);
        var resolver = new ResourceResolver();
        Register("_geometries", geometryKey, geometry);
        Register("_materials", materialKey, new ResolvedMaterial { Id = new(materialKey), Type = 5, SourceBytes = bytes });
        var world = new WorldData();
        world.Objects.Add(new() { GaoKey = 7, GeometryKey = geometryKey, MaterialKey = materialKey });
        var source = new AnimationClip
        {
            Tracks = [new() { Bone = -1, Target = 7, Keys =
            [CinematicTestEvents.Call(1, 7, 15006, 0, 0, 1), CinematicTestEvents.Call(2, 7, 15008, 0, 0, 0)] }]
        };
        var state = new CinematicMaterialState(source, world, resolver.Materials);
        Raylib_cs.Color? disabledPixel = null;
        Raylib_cs.Color? enabledPixel = null;
        Raylib_cs.Color? alphaAdjustedPixel = null;
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(160, 120, "Cinematic material verification");
        try
        {
            using var renderer = new SceneRenderer
            {
                ShowGrid = false,
                ShowGizmo = false,
                GameView = true,
                CinematicMaterials = state,
                FrustumCullingEnabled = false
            };
            renderer.Camera.Position = new(0, 0, 5);
            renderer.Camera.Target = Vector3.Zero;
            renderer.Camera.Up = Vector3.UnitY;
            foreach (float time in new[] { .5f, 1.5f, 2.5f, 1.5f })
            {
                state.Evaluate(time);
                renderer.Render(160, 120, new(), world, null, resolver);
                var image = Raylib.LoadImageFromTexture(renderer.Target.Texture);
                try
                {
                    var color = Raylib.GetImageColor(image, 80, 60);
                    if (time == .5f) disabledPixel = color;
                    else if (time == 1.5f)
                    {
                        if (enabledPixel is Raylib_cs.Color previous && !previous.Equals(color))
                            throw new InvalidDataException($"GPU material reverse seek changed the enabled pixel: {previous} to {color}.");
                        enabledPixel = color;
                    }
                    else alphaAdjustedPixel = color;
                }
                finally { Raylib.UnloadImage(image); }
            }
            AssertPixelNear(disabledPixel!.Value, new(18, 23, 20, 255), 4,
                "Disabled cinematic material should leave the scene clear color visible");
            AssertPixelNear(enabledPixel!.Value, new(138, 10, 138, 255), 4,
                "Enabled cinematic material should show the missing-texture fallback");
            if (!disabledPixel.Value.Equals(alphaAdjustedPixel!.Value))
                throw new InvalidDataException($"GPU material disable event changed the clear pixel: {disabledPixel} to {alphaAdjustedPixel}.");
            renderer.CinematicMaterials = null;
            renderer.Render(160, 120, new(), world, null, resolver);
            VerifySprite(renderer);
            VerifySimpleOpacity(renderer);
            VerifyAuthoredDiffuseColor(renderer);
            VerifyDiffuse2x(renderer);
            VerifyCinematicRliInputs(renderer);
            Console.WriteLine("PASS: GPU raw RLI/invert, computed diffuse, draw-mask controls and instance/geometry color precedence");
            VerifyCinematicFog(renderer);
            Console.WriteLine("PASS: GPU material activation, vertex-color diffuse/2x, packed cinematic RLI subtraction, opacity, sprite alpha, cinematic fog and reverse seek");
        }
        finally { Raylib.CloseWindow(); }

        void Register<T>(string field, uint key, T value) =>
            ((Dictionary<uint, T>)typeof(ResourceResolver).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(resolver)!).Add(key, value);

        static void AssertPixelNear(Raylib_cs.Color actual, Raylib_cs.Color expected, int tolerance, string message)
        {
            if (Math.Abs(actual.R - expected.R) > tolerance || Math.Abs(actual.G - expected.G) > tolerance
                || Math.Abs(actual.B - expected.B) > tolerance || Math.Abs(actual.A - expected.A) > tolerance)
                throw new InvalidDataException($"{message}: expected {expected} ±{tolerance}, observed {actual}.");
        }

        static Raylib_cs.Color Blend(Raylib_cs.Color background, Raylib_cs.Color foreground, float alpha) => new(
            (byte)MathF.Round(background.R * (1 - alpha) + foreground.R * alpha),
            (byte)MathF.Round(background.G * (1 - alpha) + foreground.G * alpha),
            (byte)MathF.Round(background.B * (1 - alpha) + foreground.B * alpha), (byte)255);

        void VerifySprite(SceneRenderer renderer)
        {
            const uint spriteGeometry = 101, spriteMaterial = 0x47000003;
            var sprite = new Geometry
            {
                Key = spriteGeometry,
                Positions = new Vector3[5],
                VertexColors = Enumerable.Repeat(0x40ffffffu, 5).ToArray(),
                Parts = [new(0, [1, 2, 3, 1, 3, 4], [])]
            };
            sprite.Billboards.Add(new(1, Vector3.Zero, 2, 2, false));
            Register("_geometries", spriteGeometry, sprite);
            var materialBytes = (byte[])bytes.Clone();
            BitConverter.GetBytes(0x00010207u).CopyTo(materialBytes, 40);
            Register("_materials", spriteMaterial, new ResolvedMaterial
            { Id = new(spriteMaterial), Type = 5, SourceBytes = materialBytes });
            world.Objects[0].PreviewHidden = true;
            world.Objects.Add(new()
            {
                GaoKey = 8,
                GeometryKey = spriteGeometry,
                MaterialKey = spriteMaterial,
                RliColors = [0x40ffffff]
            });
            renderer.Render(160, 120, new(), world, null, resolver);
            var image = Raylib.LoadImageFromTexture(renderer.Target.Texture);
            try
            {
                var pixel = Raylib.GetImageColor(image, 80, 60);
                float alpha = 64f / 255f;
                var expected = new Raylib_cs.Color(
                    (byte)MathF.Round(disabledPixel!.Value.R * (1 - alpha) + enabledPixel!.Value.R * alpha),
                    (byte)MathF.Round(disabledPixel.Value.G * (1 - alpha) + enabledPixel.Value.G * alpha),
                    (byte)MathF.Round(disabledPixel.Value.B * (1 - alpha) + enabledPixel.Value.B * alpha), (byte)255);
                AssertPixelNear(pixel, expected, 3, "SPG vertex alpha should blend the known fallback color at 64/255");
            }
            finally { Raylib.UnloadImage(image); }
        }

        void VerifySimpleOpacity(SceneRenderer renderer)
        {
            const uint simpleMaterial = 0x47000004;
            var materialBytes = new byte[36];
            BitConverter.GetBytes(3).CopyTo(materialBytes, 0);
            BitConverter.GetBytes(.5f).CopyTo(materialBytes, 20);
            Register("_materials", simpleMaterial, new ResolvedMaterial
            { Id = new(simpleMaterial), Type = 3, SourceBytes = materialBytes });
            world.Objects.Clear();
            world.Objects.Add(new() { GaoKey = 9, GeometryKey = geometryKey, MaterialKey = simpleMaterial });
            renderer.Render(160, 120, new(), world, null, resolver);
            var image = Raylib.LoadImageFromTexture(renderer.Target.Texture);
            try
            {
                var pixel = Raylib.GetImageColor(image, 80, 60);
                var expected = Blend(disabledPixel!.Value, enabledPixel!.Value, .5f);
                AssertPixelNear(pixel, expected, 3, "Simple material opacity should blend the known fallback color at 50%");
            }
            finally { Raylib.UnloadImage(image); }
        }

        void VerifyAuthoredDiffuseColor(SceneRenderer renderer)
        {
            const uint coloredGeometryKey = 102, diffuseMaterialKey = 0x47000005;
            var coloredGeometry = new Geometry
            {
                Key = coloredGeometryKey,
                Positions = [new(-2, 0, -2), new(2, 0, -2), new(0, 0, 2)],
                VertexColors = [0xff0000ff, 0xff0000ff, 0xff0000ff],
                Parts = [new(0, [0, 1, 2], [])]
            };
            var diffuseBytes = new byte[56];
            BitConverter.GetBytes(5).CopyTo(diffuseBytes, 0);
            BitConverter.GetBytes(0x00000007u).CopyTo(diffuseBytes, 40);
            Register("_geometries", coloredGeometryKey, coloredGeometry);
            Register("_materials", diffuseMaterialKey, new ResolvedMaterial
            { Id = new(diffuseMaterialKey), Type = 5, SourceBytes = diffuseBytes });
            world.Objects.Clear();
            world.Objects.Add(new() { GaoKey = 10, GeometryKey = coloredGeometryKey, MaterialKey = diffuseMaterialKey,
                DrawFlags = 0x01000048 });
            renderer.Render(160, 120, new(), world, null, resolver);
            var image = Raylib.LoadImageFromTexture(renderer.Target.Texture);
            try
            {
                var pixel = Raylib.GetImageColor(image, 80, 60);
                var expected = new Raylib_cs.Color(enabledPixel!.Value.R, (byte)0, (byte)0, (byte)255);
                AssertPixelNear(pixel, expected, 3, "Diffuse preview should tint the known fallback with authored red vertex color");
            }
            finally { Raylib.UnloadImage(image); }
        }

        void VerifyDiffuse2x(SceneRenderer renderer)
        {
            const uint doubleGeometryKey = 103, diffuse2xMaterialKey = 0x47000006;
            var halfRedGeometry = new Geometry
            {
                Key = doubleGeometryKey,
                Positions = [new(-2, 0, -2), new(2, 0, -2), new(0, 0, 2)],
                VertexColors = [0x800000ff, 0x800000ff, 0x800000ff],
                Parts = [new(0, [0, 1, 2], [])]
            };
            var diffuse2xBytes = new byte[56];
            BitConverter.GetBytes(5).CopyTo(diffuse2xBytes, 0);
            BitConverter.GetBytes(0x00006007u).CopyTo(diffuse2xBytes, 40);
            Register("_geometries", doubleGeometryKey, halfRedGeometry);
            Register("_materials", diffuse2xMaterialKey, new ResolvedMaterial
            { Id = new(diffuse2xMaterialKey), Type = 5, SourceBytes = diffuse2xBytes });
            world.Objects.Clear();
            world.Objects.Add(new() { GaoKey = 11, GeometryKey = doubleGeometryKey, MaterialKey = diffuse2xMaterialKey,
                DrawFlags = 0x01000048 });
            renderer.Render(160, 120, new(), world, null, resolver);
            var image = Raylib.LoadImageFromTexture(renderer.Target.Texture);
            try
            {
                var pixel = Raylib.GetImageColor(image, 80, 60);
                var expected = new Raylib_cs.Color(enabledPixel!.Value.R, (byte)0, (byte)0, (byte)255);
                AssertPixelNear(pixel, expected, 3, "Diffuse2X should double half-red to the known fallback red channel");
            }
            finally { Raylib.UnloadImage(image); }
        }

        void VerifyCinematicRliInputs(SceneRenderer renderer)
        {
            const uint rliGeometryKey = 104, rliMaterialKey = 0x47000009;
            Register("_geometries", rliGeometryKey, new Geometry
            {
                Key = rliGeometryKey,
                Positions = [new(-2, 0, -2), new(2, 0, -2), new(0, 0, 2)],
                VertexColors = [0xff204060, 0xff204060, 0xff204060],
                Parts = [new(0, [0, 1, 2], [])]
            });
            var rliMaterialBytes = new byte[56];
            BitConverter.GetBytes(5).CopyTo(rliMaterialBytes, 0);
            BitConverter.GetBytes(0x00003007u).CopyTo(rliMaterialBytes, 40);
            if (JadeMaterialLayer.Read(rliMaterialBytes).Single().ColorMode != 3)
                throw new InvalidDataException("RLI render check did not construct an RLI material.");
            Register("_materials", rliMaterialKey, new ResolvedMaterial
            { Id = new(rliMaterialKey), Type = 5, SourceBytes = rliMaterialBytes });
            const uint diffuseMaterialKey = rliMaterialKey + 1, invertMaterialKey = rliMaterialKey + 2;
            foreach (var (key, flags) in new[] { (diffuseMaterialKey, 0x00000007u), (invertMaterialKey, 0x00005007u) })
            {
                var materialBytes = (byte[])rliMaterialBytes.Clone();
                BitConverter.GetBytes(flags).CopyTo(materialBytes, 40);
                Register("_materials", key, new ResolvedMaterial
                { Id = new(key), Type = 5, SourceBytes = materialBytes });
            }

            const uint ownerKey = 30, sunKey = 31;
            var palettes = Enumerable.Range(0, 4)
                .Select(_ => Enumerable.Repeat(0u, 9).ToArray()).ToArray();
            palettes[0][7] = 0xff3264c8; // R=200, G=100, B=50 in Jaded's packed color.
            palettes[0][0] = 0xff103020;
            palettes[0][5] = 0xff602010;
            var controller = new CinematicEnvironmentController(ownerKey, 0, [sunKey])
            {
                SunKey = sunKey,
                NightHeight = 0,
                DawnHeight = 1,
                SunriseHeight = 2,
                DayHeight = 3,
                NightRliFactor = .5f,
                NormalPalettes = palettes
            };
            world.Objects.Clear();
            var sceneObject = new SceneObject
            {
                GaoKey = 29,
                GeometryKey = rliGeometryKey,
                MaterialKey = rliMaterialKey,
                DrawFlags = 0x01000048,
                RliColors = [0xffc080f0, 0xffc080f0, 0xffc080f0]
            };
            world.Objects.Add(sceneObject);
            world.Objects.Add(new() { GaoKey = ownerKey });
            world.Objects.Add(new() { GaoKey = sunKey, Transform = new() { Translation = new(0, 0, -.5f) } });
            var lighting = new CinematicMaterialState(new AnimationClip(), world,
                resolver.Materials, [controller]);
            lighting.Evaluate(0);
            renderer.CinematicMaterials = lighting;
            int[] rawTint = [240, 128, 192];
            int[] destination = [ScalePackedChannel(200, .5f), ScalePackedChannel(100, .5f), ScalePackedChannel(50, .5f)];
            int[] computedTint = rawTint.Select((channel, index) => Math.Max(0, channel - destination[index])).ToArray();
            CheckPixel(rawTint, "RLI must read the raw instance color, independent of world-cycle subtraction");
            sceneObject.MaterialKey = invertMaterialKey;
            CheckPixel(rawTint.Select(channel => 255 - channel).ToArray(), "InvertDiffuse must invert raw instance RLI");
            sceneObject.MaterialKey = diffuseMaterialKey;
            CheckPixel(computedTint, "Diffuse must read packed-subtracted computed RLI");
            sceneObject.DrawFlags |= 0x02000000;
            CheckPixel(rawTint, "DontScaleRLI must preserve computed diffuse color");
            sceneObject.DrawFlags &= ~0x40u;
            CheckPixel([0, 0, 0], "UseRLI off must clear the computed RLI contribution even with DontScaleRLI");
            sceneObject.MaterialKey = rliMaterialKey;
            CheckPixel(rawTint, "Direct RLI must remain available when computed UseRLI is off");
            sceneObject.RliColors = [];
            sceneObject.AppearanceRevision++;
            CheckPixel([96, 64, 32], "Geometry RLI must be restored after an instance override");
            sceneObject.MaterialKey = diffuseMaterialKey;
            sceneObject.DrawFlags = 0x01000048;
            CheckPixel([Math.Max(0, 96 - destination[0]), Math.Max(0, 64 - destination[1]),
                Math.Max(0, 32 - destination[2])], "Geometry fallback must feed computed RLI after instance cleanup");
            sceneObject.RliColors = [0xffc080f0, 0xffc080f0, 0xffc080f0];
            sceneObject.AppearanceRevision++;
            CheckPixel(computedTint, "Restoring the instance override must not retain cached geometry RLI");
            sceneObject.DrawFlags |= 0x100;
            CheckPixel(AddAmbient(computedTint, [16, 32, 96]), "UseAmbient must add the second world ambient channel after subtraction");
            sceneObject.DrawFlags |= 0x00400000;
            CheckPixel(AddAmbient(computedTint, [32, 48, 16]), "DontUseAmbient2 must select the first ambient channel");
            sceneObject.DrawFlags &= ~0x40u;
            CheckPixel([32, 48, 16], "World ambient must remain available with computed RLI disabled");
            sceneObject.MaterialKey = rliMaterialKey;
            CheckPixel(rawTint, "World ambient must not change the raw RLI material input");
            sceneObject.MaterialKey = diffuseMaterialKey;
            sceneObject.DrawFlags &= ~0x100u;
            CheckPixel([0, 0, 0], "Disabling ambient must not retain color from the preceding draw");
            const uint unpaintedGeometryKey = 105;
            Register("_geometries", unpaintedGeometryKey, new Geometry
            {
                Key = unpaintedGeometryKey,
                Positions = [new(-2, 0, -2), new(2, 0, -2), new(0, 0, 2)],
                Parts = [new(0, [0, 1, 2], [])]
            });
            sceneObject.GeometryKey = unpaintedGeometryKey;
            sceneObject.RliColors = [];
            sceneObject.DrawFlags = 0x01000108;
            CheckPixel([16, 32, 96], "Ambient must light geometry without either RLI source");
            sceneObject.MaterialKey = rliMaterialKey;
            CheckPixel([0, 0, 0], "Missing raw RLI must not borrow computed ambient color");
            renderer.CinematicMaterials = null;
            var sourceWorld = new WorldData { Lighting = new(0xff285064, 0xff643c14) };
            sourceWorld.Objects.Add(sceneObject);
            sceneObject.MaterialKey = diffuseMaterialKey;
            CheckPixel([20, 60, 100], "Serialized WOW ambient must work without a cinematic material state", sourceWorld);
            sceneObject.DrawFlags |= 0x00400000;
            CheckPixel([100, 80, 40], "Serialized WOW must retain both ambient channels", sourceWorld);
            sceneObject.DrawFlags &= ~0x00400000u;
            renderer.CinematicMaterials = lighting;
            CheckPixel([16, 32, 96], "Evaluated world-cycle ambient must override the serialized baseline", sourceWorld);
            renderer.CinematicMaterials = null;

            static int[] AddAmbient(int[] tint, int[] ambient) =>
                tint.Select((channel, index) => Math.Min(255, channel + ambient[index])).ToArray();

            void CheckPixel(int[] tint, string message, WorldData? renderWorld = null)
            {
                renderer.Render(160, 120, new(), renderWorld ?? world, null, resolver);
                var image = Raylib.LoadImageFromTexture(renderer.Target.Texture);
                try
                {
                    var pixel = Raylib.GetImageColor(image, 80, 60);
                    var fallback = enabledPixel!.Value;
                    var expected = new Raylib_cs.Color((byte)((tint[0] * fallback.R + 127) / 255),
                        (byte)((tint[1] * fallback.G + 127) / 255),
                        (byte)((tint[2] * fallback.B + 127) / 255), pixel.A);
                    AssertPixelNear(pixel, expected, 4, message);
                }
                finally { Raylib.UnloadImage(image); }
            }
        }

        static int ScalePackedChannel(int channel, float factor)
        {
            if (factor >= .98f) return channel;
            if (factor <= .02f) return 0;
            int interpoler = BitConverter.SingleToInt32Bits(factor + 49152f);
            uint scaled = 0;
            uint packedChannel = (uint)channel * 0x01010101u;
            ReadOnlySpan<(uint Mask, int Shift)> terms =
            [ (0xfefefefeu, 1), (0xfcfcfcfcu, 2), (0xf8f8f8f8u, 3), (0xf0f0f0f0u, 4),
              (0xe0e0e0e0u, 5), (0xc0c0c0c0u, 6), (0x80808080u, 7) ];
            for (int index = 0; index < terms.Length; index++)
                if ((interpoler & (128 >> index)) != 0)
                    scaled += ((packedChannel & terms[index].Mask) >> terms[index].Shift) & 0xff;
            return (int)scaled;
        }

        void VerifyCinematicFog(SceneRenderer renderer)
        {
            const uint fogOwnerKey = 20, sunKey = 21, fogMaterialKey = 0x47000007;
            var fogMaterialBytes = new byte[36];
            BitConverter.GetBytes(3).CopyTo(fogMaterialBytes, 0);
            BitConverter.GetBytes(1f).CopyTo(fogMaterialBytes, 20);
            Register("_materials", fogMaterialKey, new ResolvedMaterial
            { Id = new(fogMaterialKey), Type = 3, SourceBytes = fogMaterialBytes });
            uint[][] palettes = Enumerable.Range(0, 4)
                .Select(_ => Enumerable.Repeat(0x00000000u, 9).ToArray())
                .ToArray();
            foreach (var palette in palettes) palette[1] = 0xff0000ff;

            var controller = new CinematicEnvironmentController(fogOwnerKey, 0, [sunKey])
            {
                SunKey = sunKey,
                NightHeight = 10,
                DawnHeight = 20,
                SunriseHeight = 30,
                DayHeight = 40,
                FogNear = 0,
                FogFar = 10,
                AttackFogNear = 0,
                AttackFogFar = 10,
                NormalPalettes = palettes
            };
            world.Objects.Clear();
            world.Objects.Add(new()
            {
                GaoKey = 19,
                GeometryKey = geometryKey,
                MaterialKey = fogMaterialKey,
                DrawFlags = 0x01004008
            });
            world.Objects.Add(new() { GaoKey = fogOwnerKey });
            world.Objects.Add(new() { GaoKey = sunKey });
            var fogState = new CinematicMaterialState(new AnimationClip(), world, resolver.Materials, [controller]);
            fogState.Evaluate(0);
            renderer.CinematicMaterials = fogState;

            world.Objects[0].DrawFlags = 0x01000008;
            renderer.Render(160, 120, new(), world, null, resolver);
            var unfoggedImage = Raylib.LoadImageFromTexture(renderer.Target.Texture);
            try
            {
                var unfoggedPixel = Raylib.GetImageColor(unfoggedImage, 80, 60);
                AssertPixelNear(unfoggedPixel, enabledPixel!.Value, 3,
                    "Objects without the Fogged draw-mask must preserve the known fallback color");
            }
            finally { Raylib.UnloadImage(unfoggedImage); }

            world.Objects[0].DrawFlags = 0x01004008;
            renderer.Render(160, 120, new(), world, null, resolver);
            var image = Raylib.LoadImageFromTexture(renderer.Target.Texture);
            try
            {
                var pixel = Raylib.GetImageColor(image, 80, 60);
                var expected = Blend(enabledPixel!.Value, new((byte)255, (byte)0, (byte)0, (byte)255), .5f);
                AssertPixelNear(pixel, expected, 4, "Linear cinematic fog should blend the known fallback toward red at half depth");
            }
            finally { Raylib.UnloadImage(image); }

            const uint additiveFogMaterialKey = 0x47000008;
            var additiveFogMaterialBytes = (byte[])bytes.Clone();
            BitConverter.GetBytes(0x00054c07u).CopyTo(additiveFogMaterialBytes, 40);
            Register("_materials", additiveFogMaterialKey, new ResolvedMaterial
            { Id = new(additiveFogMaterialKey), Type = 5, SourceBytes = additiveFogMaterialBytes });
            world.Objects[0].MaterialKey = additiveFogMaterialKey;
            renderer.Render(160, 120, new(), world, null, resolver);
            var additiveFogImage = Raylib.LoadImageFromTexture(renderer.Target.Texture);
            try
            {
                var pixel = Raylib.GetImageColor(additiveFogImage, 80, 60);
                if (Math.Abs(pixel.R - pixel.G) > 12 || Math.Abs(pixel.G - pixel.B) > 12)
                    throw new InvalidDataException($"Jade additive/subtractive fog should fade toward black, observed {pixel}.");
            }
            finally { Raylib.UnloadImage(additiveFogImage); }
            renderer.CinematicMaterials = null;
        }
    }
}
