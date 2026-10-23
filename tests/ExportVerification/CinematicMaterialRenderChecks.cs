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
                    bool white = color.R > 230 && color.G > 230 && color.B > 230;
                    if (white != (time == 1.5f))
                        throw new InvalidDataException($"GPU material activation/alpha mismatch at {time}: {color}.");
                }
                finally { Raylib.UnloadImage(image); }
            }
            renderer.CinematicMaterials = null;
            renderer.Render(160, 120, new(), world, null, resolver);
            VerifySprite(renderer);
            VerifySimpleOpacity(renderer);
            VerifyAuthoredDiffuseColor(renderer);
            VerifyDiffuse2x(renderer);
            VerifyCinematicFog(renderer);
            Console.WriteLine("PASS: GPU material activation, vertex-color diffuse/2x, opacity, sprite alpha, cinematic fog and reverse seek");
        }
        finally { Raylib.CloseWindow(); }

        void Register<T>(string field, uint key, T value) =>
            ((Dictionary<uint, T>)typeof(ResourceResolver).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(resolver)!).Add(key, value);

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
                if (pixel.R < 65 || pixel.R > 95 || pixel.G < 65 || pixel.G > 95)
                    throw new InvalidDataException($"SPG alpha should blend at 64/255, observed {pixel}.");
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
                if (pixel.R is < 110 or > 145 || pixel.G is < 110 or > 145 || pixel.B is < 110 or > 145)
                    throw new InvalidDataException($"Simple material opacity should blend white at 50%, observed {pixel}.");
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
            world.Objects.Add(new() { GaoKey = 10, GeometryKey = coloredGeometryKey, MaterialKey = diffuseMaterialKey });
            renderer.Render(160, 120, new(), world, null, resolver);
            var image = Raylib.LoadImageFromTexture(renderer.Target.Texture);
            try
            {
                var pixel = Raylib.GetImageColor(image, 80, 60);
                if (pixel.R < 230 || pixel.G > 20 || pixel.B > 20)
                    throw new InvalidDataException($"Diffuse preview should preserve authored vertex colors, observed {pixel}.");
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
            world.Objects.Add(new() { GaoKey = 11, GeometryKey = doubleGeometryKey, MaterialKey = diffuse2xMaterialKey });
            renderer.Render(160, 120, new(), world, null, resolver);
            var image = Raylib.LoadImageFromTexture(renderer.Target.Texture);
            try
            {
                var pixel = Raylib.GetImageColor(image, 80, 60);
                if (pixel.R < 240 || pixel.G > 20 || pixel.B > 20)
                    throw new InvalidDataException($"Diffuse2X should double the authored point color, observed {pixel}.");
            }
            finally { Raylib.UnloadImage(image); }
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
                if (unfoggedPixel.R < 240 || unfoggedPixel.G < 240 || unfoggedPixel.B < 240)
                    throw new InvalidDataException($"Objects without the Fogged draw-mask must not receive cinematic fog, observed {unfoggedPixel}.");
            }
            finally { Raylib.UnloadImage(unfoggedImage); }

            world.Objects[0].DrawFlags = 0x01004008;
            renderer.Render(160, 120, new(), world, null, resolver);
            var image = Raylib.LoadImageFromTexture(renderer.Target.Texture);
            try
            {
                var pixel = Raylib.GetImageColor(image, 80, 60);
                if (pixel.R < 240 || pixel.G is < 105 or > 150 || pixel.B is < 105 or > 150)
                    throw new InvalidDataException($"Linear cinematic fog should blend white toward red at half depth, observed {pixel}.");
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
