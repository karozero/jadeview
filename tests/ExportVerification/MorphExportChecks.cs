using System.Numerics;
using JadeView;

internal static class MorphExportChecks
{
    internal static void VerifyActor(GlbReader glb, SceneActor actor, ResourceResolver resolver)
    {
        var clip = actor.Clip;
        actor.Clip = null;
        actor.Evaluate(0);
        var bindPose = actor.Members.ToDictionary(member => member, member => member.PreviewMatrix!.Value);
        actor.Clip = clip;
        actor.Evaluate(0);
        foreach (var node in glb.Root.GetProperty("nodes").EnumerateArray())
        {
            if (!node.TryGetProperty("skin", out _)) continue;
            uint key = Convert.ToUInt32(node.GetProperty("extras").GetProperty("gaoKey").GetString(), 16);
            var source = actor.Members.First(member => member.GaoKey == key);
            var modifier = source.Modifiers.SingleOrDefault(modifier => modifier.Type == 4);
            if (modifier == null) continue;
            var geometry = resolver.Geometries[source.RenderGeometryKey];
            var morph = FaceMorphReader.Read(modifier.Data, geometry.Positions.Length);
            var mesh = glb.Root.GetProperty("meshes")[node.GetProperty("mesh").GetInt32()];
            Check(mesh.GetProperty("weights").GetArrayLength() == morph.Groups.Length, "Morph slots were dropped");
            Check(mesh.GetProperty("weights").EnumerateArray().All(weight => weight.GetSingle() == 0), "Unproven channel values applied as weights");
            int partIndex = 0;
            foreach (var primitive in mesh.GetProperty("primitives").EnumerateArray())
                VerifyPrimitive(glb, primitive, geometry.Parts.Where(part => part.VertexIndices.Length > 0).ElementAt(partIndex++),
                    morph, bindPose[source]);
        }
    }

    private static void VerifyPrimitive(GlbReader glb, System.Text.Json.JsonElement primitive, Geometry.Part part,
        FaceMorphData morph, Matrix4x4 meshBind)
    {
        var indices = glb.Accessor(primitive.GetProperty("indices").GetInt32());
        var targets = primitive.GetProperty("targets");
        Check(targets.GetArrayLength() == morph.Groups.Length, "Inconsistent target count across primitives");
        foreach (var group in morph.Groups)
        {
            var deltas = glb.Accessor(targets[group.Index].GetProperty("POSITION").GetInt32());
            var sourceDeltas = group.AccumulatedDeltas();
            for (int corner = 0; corner < indices.Length; corner++)
            {
                var expected = Geometry.ToRender(Vector3.TransformNormal(sourceDeltas.GetValueOrDefault(part.VertexIndices[corner]), meshBind));
                var actual = GlbReader.Vector(deltas[(int)indices[corner][0]]);
                Check(Vector3.Distance(expected, actual) < .000001f, $"Morph {group.Index} delta differs at corner {corner}");
            }
        }
    }

    private static void Check(bool passed, string message) { if (!passed) throw new Exception(message); }
}
