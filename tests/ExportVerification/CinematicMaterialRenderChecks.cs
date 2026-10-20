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
            Console.WriteLine("PASS: GPU source-disabled material activation, timed alpha, cache reuse and reverse seek");
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
    }
}
