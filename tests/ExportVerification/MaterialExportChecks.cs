using System.Numerics;
using System.Reflection;
using JadeView;

internal static class MaterialExportChecks
{
    internal static void VerifyMaterials(string output)
    {
        VerifySpecularSourceColor();
        var resolver = new ResourceResolver();
        var geometry = new Geometry { Key = 701, Positions = [Vector3.Zero, Vector3.UnitX, Vector3.UnitZ],
            Parts = [new(0, [0, 1, 2], [])] };
        var bytes = new byte[56];
        BitConverter.GetBytes(5).CopyTo(bytes, 0);
        BitConverter.GetBytes(0xFFAACCEE).CopyTo(bytes, 12); // ul_Diffuse must not drive constant-color mode.
        BitConverter.GetBytes(0xFF204080u).CopyTo(bytes, 16);
        BitConverter.GetBytes((ushort)0xF8D0).CopyTo(bytes, 38);
        BitConverter.GetBytes(0x00612404u).CopyTo(bytes, 40);
        BitConverter.GetBytes(703u).CopyTo(bytes, 52);
        var material = new ResolvedMaterial { Type = 5, SourceBytes = bytes };
        Register(resolver, "_geometries", geometry.Key, geometry);
        Register(resolver, "_materials", 702, material);
        var source = new SceneObject { Name = "Bone-projected tinted material", GeometryKey = geometry.Key,
            MaterialKey = 702, ActorNode = new() { Matrices = [Matrix4x4.CreateTranslation(2, 0, 3)] },
            Transform = new() { Translation = new(10, 20, 30) } };
        var world = new WorldData();
        world.Objects.Add(source);
        string path = Path.Combine(output, "bone-projection-material.glb");
        SceneExporter.Export(path, world, resolver, new() { Textures = false });
        using var glb = new GlbReader(path);
        var primitive = glb.Root.GetProperty("meshes")[0].GetProperty("primitives")[0];
        var uvs = glb.Accessor(primitive.GetProperty("attributes").GetProperty("TEXCOORD_0").GetInt32());
        Check(MathF.Abs(uvs[0][0] + 2) < .0001f && MathF.Abs(uvs[0][1] - 4) < .0001f,
            "Bone projection incorrectly used object/world space");
        var exported = glb.Root.GetProperty("materials")[0];
        var color = exported.GetProperty("pbrMetallicRoughness").GetProperty("baseColorFactor")
            .EnumerateArray().Select(value => value.GetSingle()).ToArray();
        float[] expected = [128 / 255f, 64 / 255f, 32 / 255f, 1f];
        Check(color.Zip(expected).All(pair => MathF.Abs(pair.First - pair.Second) < .0001f),
            "Material color or alpha override was lost");
        Check(exported.GetProperty("alphaMode").GetString() == "BLEND", "Transparent material became opaque");
        Console.WriteLine("PASS: bone-projected UV, material tint and independent alpha override");
        VerifyProceduralTextureMetadata(output);
        VerifyVertexColors(world, resolver, geometry, bytes, output);
    }

    private static void VerifySpecularSourceColor()
    {
        byte[] materialBytes = new byte[20];
        BitConverter.GetBytes(0xFFAACCEEu).CopyTo(materialBytes, 12);
        BitConverter.GetBytes(0x80402010u).CopyTo(materialBytes, 16);
        var singleMaterial = new ResolvedMaterial { Type = 3, SourceBytes = materialBytes };
        var multiTextureMaterial = new ResolvedMaterial { Type = 5, SourceBytes = materialBytes };
        Check(singleMaterial.Specular == 0x80402010u && multiTextureMaterial.Specular == 0x80402010u,
            "Single and multi-texture materials must expose ul_Specular from the native material header.");
        Vector4 expectedConstant = new(0x10 / 255f, 0x20 / 255f, 0x40 / 255f, 1);
        Check(MaterialLayerTint.ReadConstant(singleMaterial) == expectedConstant
            && MaterialLayerTint.ReadConstant(multiTextureMaterial) == expectedConstant,
            "Constant-color mode must use ul_Specular RGB, ignore ul_Diffuse, and force alpha to one.");
        Check(new ResolvedMaterial { Type = 4, SourceBytes = materialBytes }.Specular == null
            && new ResolvedMaterial { Type = 5, SourceBytes = materialBytes[..19] }.Specular == null,
            "Multi-material containers and truncated records must not be interpreted as single-material specular colors.");
        Console.WriteLine("PASS: single and multi-texture source specular color offsets and invalid-layout handling");
    }

    private static void VerifyProceduralTextureMetadata(string output)
    {
        var resolver = new ResourceResolver();
        var geometry = new Geometry { Key = 711, Positions = [Vector3.Zero, Vector3.UnitX, Vector3.UnitZ],
            Parts = [new(0, [0, 1, 2], [])] };
        byte[] materialBytes = new byte[36];
        BitConverter.GetBytes(3).CopyTo(materialBytes, 0);
        BitConverter.GetBytes(0xBF000F07u).CopyTo(materialBytes, 28);
        Register(resolver, "_geometries", geometry.Key, geometry);
        Register(resolver, "_materials", 712, new ResolvedMaterial { Type = 3, SourceBytes = materialBytes });
        Register(resolver, "_proceduralTextures", 0xBF000F07u,
            new ResolvedProceduralTexture(0xBF000F07, "FF805833", 0x2429, 96, 3, 128, 128,
                0x61006334, "propaganda_01.bik"));
        var world = new WorldData();
        world.Objects.Add(new SceneObject { Name = "Video surface", GeometryKey = geometry.Key, MaterialKey = 712 });
        string path = Path.Combine(output, "procedural-video-material.glb");
        var result = SceneExporter.Export(path, world, resolver, new() { Textures = true });
        using var glb = new GlbReader(path);
        var material = glb.Root.GetProperty("materials")[0];
        var video = material.GetProperty("extras").GetProperty("jadeProceduralTexture");
        Check(video.GetProperty("key").GetString() == "BF000F07"
            && video.GetProperty("kind").GetString() == "Video stream"
            && video.GetProperty("mediaKey").GetString() == "61006334"
            && video.GetProperty("mediaName").GetString() == "propaganda_01.bik"
            && video.GetProperty("width").GetInt32() == 128
            && video.GetProperty("height").GetInt32() == 128
            && video.GetProperty("scope").GetString() == "FF805833",
            "GLB material extras must preserve a procedural video's descriptor and linked media resource.");
        Check(result.Warnings.Any(warning => warning.Contains("GLB stores source metadata", StringComparison.Ordinal))
            && !result.Warnings.Any(warning => warning.Contains("unresolved texture BF000F07", StringComparison.Ordinal)),
            "Procedural video exports should explain the playback limitation instead of calling the source texture unresolved.");
        Console.WriteLine("PASS: GLB preserves procedural video descriptor and linked Bink resource metadata");
    }

    private static void VerifyVertexColors(WorldData world, ResourceResolver resolver, Geometry geometry,
        byte[] materialBytes, string output)
    {
        geometry.VertexColors = [0x80402010, 0xFF80A0C0, 0x204080FF];
        foreach (int mode in new[] { 3, 5 })
        {
            bool overrideAlpha = mode == 5;
            uint flags = 0x00610004u | (uint)mode << 12 | (overrideAlpha ? 0x400u : 0);
            BitConverter.GetBytes(flags).CopyTo(materialBytes, 40);
            string path = Path.Combine(output, $"vertex-colors-{mode}.glb");
            SceneExporter.Export(path, world, resolver, new() { Textures = false });
            using var glb = new GlbReader(path);
            var primitive = glb.Root.GetProperty("meshes")[0].GetProperty("primitives")[0];
            var colors = glb.Accessor(primitive.GetProperty("attributes").GetProperty("COLOR_0").GetInt32());
            for (int vertex = 0; vertex < colors.Length; vertex++)
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    float source = (geometry.VertexColors[vertex] >> (8 * channel) & 255) / 255f;
                    float expected = mode == 5 ? 1 - source : source;
                    Check(MathF.Abs(colors[vertex][channel] - expected) < .0001f, "Vertex RGB was lost or inverted incorrectly");
                }
                float alpha = overrideAlpha ? 1 : (geometry.VertexColors[vertex] >> 24) / 255f;
                Check(MathF.Abs(colors[vertex][3] - alpha) < .0001f, "Vertex alpha or alpha override was applied incorrectly");
            }
        }
        Console.WriteLine("PASS: original and inverted vertex colors with alpha override");
        VerifyInstanceVertexColors(world, resolver, geometry, materialBytes, output);
    }

    private static void VerifyInstanceVertexColors(WorldData world, ResourceResolver resolver, Geometry geometry,
        byte[] materialBytes, string output)
    {
        uint[] originalColors = (uint[])geometry.VertexColors.Clone();
        uint[] instanceColors = [0xff102030, 0x80406080]; // The third vertex must fall back to geometry RLI.
        world.Objects[0].RliColors = instanceColors;
        foreach (int mode in new[] { 3, 5 })
        {
            BitConverter.GetBytes(7u | (uint)mode << 12).CopyTo(materialBytes, 40);
            string path = Path.Combine(output, $"instance-vertex-colors-{mode}.glb");
            SceneExporter.Export(path, world, resolver, new() { Textures = false });
            using var glb = new GlbReader(path);
            var attributes = glb.Root.GetProperty("meshes")[0].GetProperty("primitives")[0].GetProperty("attributes");
            var colors = glb.Accessor(attributes.GetProperty("COLOR_0").GetInt32());
            for (int vertex = 0; vertex < colors.Length; vertex++)
            {
                uint packed = vertex < instanceColors.Length ? instanceColors[vertex] : originalColors[vertex];
                float[] expected = [(packed & 255) / 255f, (packed >> 8 & 255) / 255f,
                    (packed >> 16 & 255) / 255f, (packed >> 24) / 255f];
                if (mode == 5) for (int channel = 0; channel < 3; channel++) expected[channel] = 1 - expected[channel];
                Check(colors[vertex].Zip(expected).All(pair => Math.Abs(pair.First - pair.Second) < .0001f),
                    "GLB raw/inverted RLI must prioritize instance RGB/alpha and retain geometry fallback.");
            }
        }
        world.Objects[0].RliColors = [];
        Check(geometry.VertexColors.SequenceEqual(originalColors), "Instance export mutated shared geometry RLI.");
        Console.WriteLine("PASS: GLB instance RLI precedence, partial geometry fallback, independent alpha and source isolation");
    }

    internal static void VerifySallyMaterials(GlbReader glb)
    {
        int eyes = 0, hairMaterials = 0;
        var materials = glb.Root.GetProperty("materials");
        foreach (var mesh in glb.Root.GetProperty("meshes").EnumerateArray())
        foreach (var primitive in mesh.GetProperty("primitives").EnumerateArray())
        {
            var material = materials[primitive.GetProperty("material").GetInt32()];
            string name = material.GetProperty("name").GetString()!;
            bool eye = name.EndsWith("62002FC4") || name.EndsWith("62002FCE");
            bool hair = name.EndsWith("62002FBF") || name.EndsWith("6200303A");
            if (eye)
            {
                Check(material.TryGetProperty("extras", out var extras) && extras.GetProperty("bakedMaskedLayers").GetBoolean(),
                    "Sally eye material lost its masked overlay bake");
                var uvs = glb.Accessor(primitive.GetProperty("attributes").GetProperty("TEXCOORD_0").GetInt32());
                Check(uvs.SelectMany(uv => uv).All(value => MathF.Abs(value) < 4),
                    "Sally eye UVs are displaced into scene coordinates");
                eyes++;
            }
            if (hair)
            {
                var color = material.GetProperty("pbrMetallicRoughness").GetProperty("baseColorFactor");
                Check(color[0].GetSingle() < .01f && color[1].GetSingle() < .01f && color[2].GetSingle() < .01f,
                    "Sally hair lost its dark material tint");
                hairMaterials++;
            }
        }
        Check(eyes == 2 && hairMaterials == 2, "Expected Sally eye/hair materials were not exported");
    }

    private static void Register<T>(ResourceResolver resolver, string field, uint key, T value) =>
        ((Dictionary<uint, T>)typeof(ResourceResolver).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(resolver)!).Add(key, value);

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new Exception(message);
    }
}
