using System.Numerics;
using System.Text.Json;
using JadeView;

internal static class MilitaryMaterialChecks
{
    internal static void Verify(GlbReader glb, SceneActor actor, SceneObject head, ResourceResolver resolver, string output)
    {
        var headMesh = glb.Root.GetProperty("meshes").EnumerateArray().Single(mesh => mesh.GetProperty("name").GetString() == head.Name);
        foreach (var weight in headMesh.GetProperty("weights").EnumerateArray())
            Check(weight.GetSingle() == 0, "Neutral military face exported with a leftover morph pose");
        var materials = glb.Root.GetProperty("materials");
        int eyes = headMesh.GetProperty("primitives").EnumerateArray().Count(primitive =>
        {
            var material = materials[primitive.GetProperty("material").GetInt32()];
            return material.TryGetProperty("extras", out var extras) &&
                extras.TryGetProperty("bakedMaskedLayers", out var baked) && baked.GetBoolean();
        });
        Check(eyes == 2, "Military eye materials lost the face alpha overlay");
        foreach (var material in materials.EnumerateArray().Where(material =>
            material.GetProperty("name").GetString()!.EndsWith("16002AE6") ||
            material.GetProperty("name").GetString()!.EndsWith("16002AE7")))
        {
            var pbr = material.GetProperty("pbrMetallicRoughness");
            Check(material.GetProperty("extras").GetProperty("bakedReflectionDetail").GetBoolean(),
                "Military armor lost its reflection/detail composition");
            Check(!material.TryGetProperty("alphaMode", out _), "Opaque armor became transparent");
        }
        actor.Evaluate(0);
        var geometry = resolver.Geometries[head.RenderGeometryKey];
        var positions = actor.Skin(head, geometry)!;
        var weightSums = new float[positions.Length];
        foreach (var binding in geometry.Skin)
        foreach (uint packed in binding.PackedWeights)
            weightSums[packed & 65535] += BitConverter.UInt32BitsToSingle(packed);
        var reference = positions.Select((position, vertex) =>
        {
            var normalized = weightSums[vertex] > 0 ? position / weightSums[vertex] : position;
            return Geometry.ToRender(Vector3.Transform(normalized, head.PreviewMatrix!.Value));
        }).ToArray();
        File.WriteAllText(Path.Combine(output, "military-head-reference.json"),
            JsonSerializer.Serialize(new { Head = head.Name, Positions = reference }, new JsonSerializerOptions { IncludeFields = true }));
        Console.WriteLine("PASS: neutral military face, both layered eye materials and source vertex reference");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static void WriteSource(SceneObject head, SceneObject hair, ResourceResolver resolver, string output)
    {
        var sourceMaterials = new[] { head, hair }.Select(member => new
        {
            member.Name, member.DrawFlags, member.Flags, Geometry = member.RenderGeometryKey.ToString("X8"),
            Parts = resolver.Geometries[member.RenderGeometryKey].Parts.Select(part => part.MaterialId),
            Materials = new[] { resolver.Materials[member.MaterialKey] }
                .Concat(resolver.Materials[member.MaterialKey].SubMaterialKeys.Select(key => resolver.Materials[key]))
                .Select(material => new { material.Id, material.Type, Header = Convert.ToHexString((material.SourceBytes ?? []).Take(36).ToArray()),
                    Layers = JadeMaterialLayer.Read(material.SourceBytes) })
        });
        File.WriteAllText(Path.Combine(output, "military-hd-materials-source.json"),
            System.Text.Json.JsonSerializer.Serialize(sourceMaterials, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
