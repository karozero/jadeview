using System.Numerics;
using System.Reflection;
using JadeView;

internal static class MaterialExportChecks
{
    internal static void VerifyMaterials(string output)
    {
        var resolver = new ResourceResolver();
        var geometry = new Geometry { Key = 701, Positions = [Vector3.Zero, Vector3.UnitX, Vector3.UnitZ],
            Parts = [new(0, [0, 1, 2], [])] };
        var bytes = new byte[56];
        BitConverter.GetBytes(5).CopyTo(bytes, 0);
        BitConverter.GetBytes(0xFF204080u).CopyTo(bytes, 12);
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
        VerifyVertexColors(world, resolver, geometry, bytes, output);
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
