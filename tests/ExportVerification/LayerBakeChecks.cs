using System.Drawing;
using System.Numerics;
using System.Reflection;
using JadeView;

internal static class LayerBakeChecks
{
    internal static void Verify(string output)
    {
        var resolver = new ResourceResolver();
        var geometry = new Geometry { Key = 801,
            Positions = [Vector3.Zero, new(.1f, 0, 0), new(0, 0, .1f)], Uvs = [new(.75f, .5f)],
            Parts = [new(0, [0, 1, 2, 0, 2, 1], [0, 0, 0, 0, 0, 0])] };
        Register(resolver, "_geometries", geometry.Key, geometry);
        RegisterTexture(resolver, 811, [255, 0, 0, 255, 0, 255, 0, 255]);
        RegisterTexture(resolver, 812, [255, 255, 255, 0, 255, 255, 255, 255]);
        RegisterTexture(resolver, 813, [255, 255, 0, 255, 0, 0, 255, 255]);
        var bytes = new byte[96];
        BitConverter.GetBytes(5).CopyTo(bytes, 0);
        Layer(bytes, 36, 0x10, 0x00600004, 811);
        Layer(bytes, 56, 0x8000, 0x444, 812);
        Layer(bytes, 76, 0, 0x00030087, 813);
        Register(resolver, "_materials", 802, new ResolvedMaterial { Type = 5, SourceBytes = bytes });
        var world = new WorldData();
        world.Objects.Add(new() { Name = "Masked overlay fixture", GeometryKey = geometry.Key, MaterialKey = 802 });
        string path = Path.Combine(output, "masked-overlay.glb");
        SceneExporter.Export(path, world, resolver, new() { Textures = true });
        using var glb = new GlbReader(path);
        var material = glb.Root.GetProperty("materials")[0];
        Check(material.GetProperty("extras").GetProperty("bakedMaskedLayers").GetBoolean(), "Layers were not baked");
        using var stream = new MemoryStream(glb.Image(0));
        using var bitmap = new Bitmap(stream);
        var pixel = bitmap.GetPixel(5, 5);
        // The 5-bit constant 16/31 is slightly above one half.
        Check(pixel.R == 132 && pixel.G == 0 && pixel.B == 123 && pixel.A == 255,
            $"Layer blend or independent UV sampling failed: {pixel}");
        var primitive = glb.Root.GetProperty("meshes")[0].GetProperty("primitives")[0];
        var positions = glb.Accessor(primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32());
        Check(positions.Length == 6, "Triangle atlas merged corners from separate tiles");
        for (int corner = 0; corner < 6; corner++)
            Check(Vector3.Distance(GlbReader.Vector(positions[corner]), Geometry.ToRender(geometry.Positions[geometry.Parts[0].VertexIndices[corner]])) < .00001f,
                "Baking modified source geometry");
        Console.WriteLine("PASS: masked layer blending, independent projected/stored UVs and atlas corner seams");
        VerifyAlphaOverlay(world, resolver, output);
        VerifyReflectionDetail(world, resolver, geometry, output);
    }

    private static void VerifyAlphaOverlay(WorldData world, ResourceResolver resolver, string output)
    {
        var bytes = new byte[76];
        BitConverter.GetBytes(5).CopyTo(bytes, 0);
        Layer(bytes, 36, 0x10, 0x00600004, 811);
        Layer(bytes, 56, 0x8000, 0x00010404, 812);
        Register(resolver, "_materials", 803, new ResolvedMaterial { Type = 5, SourceBytes = bytes });
        world.Objects[0].MaterialKey = 803;
        string path = Path.Combine(output, "alpha-overlay.glb");
        SceneExporter.Export(path, world, resolver, new() { Textures = true });
        using var glb = new GlbReader(path);
        Check(glb.Root.GetProperty("materials")[0].GetProperty("extras").GetProperty("bakedMaskedLayers").GetBoolean(),
            "Two-pass alpha overlay was omitted");
        using var stream = new MemoryStream(glb.Image(0));
        using var bitmap = new Bitmap(stream);
        var pixel = bitmap.GetPixel(5, 5);
        Check(pixel.R == 255 && pixel.G == 132 && pixel.B == 132 && pixel.A == 255,
            $"Source alpha overlay or its constant alpha override was not applied: {pixel}");
        Console.WriteLine("PASS: two-pass source alpha overlay, independent UVs and alpha override");

        world.Objects[0].RliColors = [0x800000FF, 0x800000FF, 0x800000FF];
        string instancePath = Path.Combine(output, "alpha-overlay-instance.glb");
        SceneExporter.Export(instancePath, world, resolver, new() { Textures = true });
        using var instance = new GlbReader(instancePath);
        using var instanceStream = new MemoryStream(instance.Image(0));
        using var instanceImage = new Bitmap(instanceStream);
        var instancePixel = instanceImage.GetPixel(5, 5);
        Check(instancePixel.R == 255 && instancePixel.G == 0 && instancePixel.B == 0 && instancePixel.A == 255,
            $"Instance RLI tint was dropped from the source-alpha overlay: {instancePixel}");
        Console.WriteLine("PASS: source-alpha overlays bake per-instance RLI color and alpha");
        world.Objects[0].RliColors = [];
    }

    private static void VerifyReflectionDetail(WorldData world, ResourceResolver resolver, Geometry geometry, string output)
    {
        var bytes = new byte[76];
        BitConverter.GetBytes(5).CopyTo(bytes, 0);
        RegisterTexture(resolver, 814, [0, 0, 0, 255, 0, 0, 0, 255]);
        Layer(bytes, 36, 0, 0x00200004, 811);
        Layer(bytes, 56, 0x8000, 0x00020484, 814);
        Register(resolver, "_materials", 804, new ResolvedMaterial { Type = 5, SourceBytes = bytes });
        world.Objects[0].MaterialKey = 804;
        string path = Path.Combine(output, "reflection-detail.glb");
        SceneExporter.Export(path, world, resolver, new() { Textures = true });
        using var glb = new GlbReader(path);
        var material = glb.Root.GetProperty("materials")[0];
        Check(!material.TryGetProperty("alphaMode", out _), "Reflection coefficient became surface transparency");
        using var stream = new MemoryStream(glb.Image(0));
        using var bitmap = new Bitmap(stream);
        Check(material.GetProperty("extras").GetProperty("bakedReflectionDetail").GetBoolean(),
            "Reflection/detail layers were not composited");
        var pixel = bitmap.GetPixel(5, 5);
        Check(pixel.R == 66 && pixel.G == 66 && pixel.B == 0 && pixel.A == 255,
            $"Reflection contribution was lost or its alpha was applied incorrectly: {pixel}");
        Check(material.GetProperty("pbrMetallicRoughness").GetProperty("baseColorFactor")[3].GetSingle() == 1,
            "Reflection alpha override leaked into base opacity");
        var primitive = glb.Root.GetProperty("meshes")[0].GetProperty("primitives")[0];
        var uvs = glb.Accessor(primitive.GetProperty("attributes").GetProperty("TEXCOORD_0").GetInt32());
        Check(uvs.SelectMany(uv => uv).All(value => value >= 0 && value <= 1), "Reflection atlas UVs are outside the image");
        byte[] originalImage = glb.Image(0);
        world.Objects[0].Transform.Translation = new(30, -20, 40);
        string movedPath = Path.Combine(output, "reflection-detail-moved.glb");
        SceneExporter.Export(movedPath, world, resolver, new() { Textures = true });
        using var moved = new GlbReader(movedPath);
        Check(moved.Image(0).SequenceEqual(originalImage), "Scene translation shifted the reference reflection");
        RegisterTexture(resolver, 815, [20, 40, 60, 255, 20, 40, 60, 255]);
        BitConverter.GetBytes(0xFF808080u).CopyTo(bytes, 12);
        Layer(bytes, 36, 0, 0x00202004, 811);
        Layer(bytes, 56, 0, 0x00023084, 815);
        geometry.VertexColors = [0x80808080, 0x80808080, 0x80808080];
        Register(resolver, "_materials", 805, new ResolvedMaterial { Type = 5, SourceBytes = bytes });
        world.Objects[0].MaterialKey = 805;
        string tintedPath = Path.Combine(output, "reflection-detail-tinted.glb");
        SceneExporter.Export(tintedPath, world, resolver, new() { Textures = true });
        using var tinted = new GlbReader(tintedPath);
        using var tintedStream = new MemoryStream(tinted.Image(0));
        using var tintedImage = new Bitmap(tintedStream);
        var tintedPixel = tintedImage.GetPixel(5, 5);
        Check(tintedPixel.R == 42 && tintedPixel.G == 52 && tintedPixel.B == 30,
            $"Constant reflection tint, painted detail RGB/alpha or additive blending was lost: {tintedPixel}");
        var tintedAttributes = tinted.Root.GetProperty("meshes")[0].GetProperty("primitives")[0].GetProperty("attributes");
        Check(!tintedAttributes.TryGetProperty("COLOR_0", out _), "Vertex tint was applied twice to the baked atlas");
        world.Objects[0].RliColors = [0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF];
        string instancePath = Path.Combine(output, "reflection-detail-instance.glb");
        SceneExporter.Export(instancePath, world, resolver, new() { Textures = true });
        using var instance = new GlbReader(instancePath);
        using var instanceStream = new MemoryStream(instance.Image(0));
        using var instanceImage = new Bitmap(instanceStream);
        var instancePixel = instanceImage.GetPixel(5, 5);
        Check(instancePixel.R == 84 && instancePixel.G == 104 && instancePixel.B == 60,
            $"Instance paint did not override geometry paint in reflection/detail baking: {instancePixel}");
        Console.WriteLine("PASS: reflection plus black detail, constant alpha, opaque atlas and placement independence");
    }

    private static void Layer(byte[] bytes, int offset, ushort custom, uint flags, uint texture)
    {
        BitConverter.GetBytes(custom).CopyTo(bytes, offset + 2);
        BitConverter.GetBytes(flags).CopyTo(bytes, offset + 4);
        BitConverter.GetBytes(texture).CopyTo(bytes, offset + 16);
    }

    private static void RegisterTexture(ResourceResolver resolver, uint key, byte[] rgba)
    {
        var tga = new byte[18 + rgba.Length];
        tga[2] = 2; tga[12] = 2; tga[14] = 1; tga[16] = 32; tga[17] = 0x28;
        for (int pixel = 0; pixel < 2; pixel++)
        {
            int source = pixel * 4, target = 18 + source;
            tga[target] = rgba[source + 2]; tga[target + 1] = rgba[source + 1];
            tga[target + 2] = rgba[source]; tga[target + 3] = rgba[source + 3];
        }
        Register(resolver, "_textures", key, new ResolvedTexture { Width = 2, Height = 1, TgaData = tga });
    }

    private static void Register<T>(ResourceResolver resolver, string field, uint key, T value) =>
        ((Dictionary<uint, T>)typeof(ResourceResolver).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(resolver)!).Add(key, value);
    private static void Check(bool passed, string message) { if (!passed) throw new Exception(message); }
}
