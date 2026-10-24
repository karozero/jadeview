using System.Drawing;
using System.Numerics;
using System.Reflection;

namespace JadeView;

/// <summary>
/// End-to-end GLB check for computed diffuse tints in the three-layer destination-alpha
/// mask composition, including the mask's local alpha override and tinted overlay pass.
/// </summary>
internal static class ThreeLayerComputedBakeChecks
{
    internal static void Verify(string output)
    {
        const uint geometryKey = 0x821;
        const uint materialKey = 0x822;
        const uint baseTextureKey = 0x831;
        const uint maskTextureKey = 0x832;
        const uint overlayTextureKey = 0x833;
        const uint useRli = 0x40;
        const uint useAmbient = 0x100;

        var resolver = new ResourceResolver();
        var geometry = new Geometry
        {
            Key = geometryKey,
            Positions = [Vector3.Zero, new(.1f, 0, 0), new(0, 0, .1f)],
            Uvs = [new(.75f, .5f)],
            VertexColors = [0xff804020, 0xff804020, 0xff804020],
            Parts = [new(0, [0, 1, 2], [0, 0, 0])]
        };
        Register(resolver, "_geometries", geometryKey, geometry);
        RegisterTexture(resolver, baseTextureKey, [255, 255, 255, 255]);
        RegisterTexture(resolver, maskTextureKey, [255, 255, 255, 255]);
        RegisterTexture(resolver, overlayTextureKey, [255, 255, 255, 255]);

        byte[] materialBytes = new byte[96];
        BitConverter.GetBytes(5).CopyTo(materialBytes, 0);
        WriteLayer(materialBytes, 36, custom: 0, flags: 0x00000004, baseTextureKey);
        // The mask layer's 5-bit local alpha is 16/31. The atlas must apply it once.
        WriteLayer(materialBytes, 56, custom: 0x8000, flags: 0x00000444, maskTextureKey);
        // Mode 6 doubles computed RGB, making this overlay tint observable in the atlas.
        WriteLayer(materialBytes, 76, custom: 0, flags: 0x00036087, overlayTextureKey);
        Register(resolver, "_materials", materialKey,
            new ResolvedMaterial { Type = 5, SourceBytes = materialBytes });

        var world = new WorldData
        {
            Name = "three layer computed bake",
            Lighting = new WorldLighting(0xff010101, 0xff101010)
        };
        world.Objects.Add(new SceneObject
        {
            Name = "Computed destination-alpha overlay",
            GeometryKey = geometryKey,
            MaterialKey = materialKey,
            DrawFlags = 0x01000008 | useRli | useAmbient
        });

        string path = Path.Combine(output, "three-layer-computed-bake.glb");
        SceneExporter.Export(path, world, resolver, new() { Textures = true });
        using var glb = new GlbReader(path);

        var exportedMaterial = glb.Root.GetProperty("materials")[0];
        Check(exportedMaterial.GetProperty("extras").GetProperty("bakedMaskedLayers").GetBoolean(),
            "The three-layer destination-alpha material should be baked into a GLB atlas.");
        using var stream = new MemoryStream(glb.Image(0));
        using var atlas = new Bitmap(stream);
        Color pixel = atlas.GetPixel(5, 5);
        // Computed base color: raw (32,64,128) + ambient2 (16,16,16) = (48,80,144).
        // Computed mode-6 overlay tint: (96,160,255). Mask local alpha is 16/31.
        // Therefore each atlas channel is base*16/31 + overlay*15/31.
        Check(pixel.R == 71 && pixel.G == 119 && pixel.B == 198 && pixel.A == 255,
            $"Computed diffuse or one-time mask alpha/overlay tint was lost or applied twice: {pixel}.");

        var attributes = glb.Root.GetProperty("meshes")[0].GetProperty("primitives")[0]
            .GetProperty("attributes");
        Check(!attributes.TryGetProperty("COLOR_0", out _),
            "Computed per-layer tints are already baked into the atlas and must not be multiplied again as vertex colors.");
    }

    private static void WriteLayer(byte[] bytes, int offset, ushort custom, uint flags, uint textureKey)
    {
        BitConverter.GetBytes(custom).CopyTo(bytes, offset + 2);
        BitConverter.GetBytes(flags).CopyTo(bytes, offset + 4);
        BitConverter.GetBytes(textureKey).CopyTo(bytes, offset + 16);
    }

    private static void RegisterTexture(ResourceResolver resolver, uint key, byte[] rgba)
    {
        var tga = new byte[18 + 2 * 4];
        tga[2] = 2;
        tga[12] = 2;
        tga[14] = 1;
        tga[16] = 32;
        tga[17] = 0x28;
        for (int pixel = 0; pixel < 2; pixel++)
        {
            int source = rgba.Length == 4 ? 0 : pixel * 4;
            int target = 18 + pixel * 4;
            tga[target] = rgba[source + 2];
            tga[target + 1] = rgba[source + 1];
            tga[target + 2] = rgba[source];
            tga[target + 3] = rgba[source + 3];
        }
        Register(resolver, "_textures", key,
            new ResolvedTexture { Width = 2, Height = 1, TgaData = tga });
    }

    private static void Register<T>(ResourceResolver resolver, string field, uint key, T value) =>
        ((Dictionary<uint, T>)typeof(ResourceResolver).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(resolver)!).Add(key, value);

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidDataException(message);
    }
}
