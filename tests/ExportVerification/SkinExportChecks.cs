using System.Numerics;
using JadeView;

internal static class SkinExportChecks
{
    internal static void VerifySkin(GlbReader glb, SceneActor actor, SceneObject source, Geometry geometry, float[] times,
        Matrix4x4? placement = null, int? selectedNode = null, int partIndex = 0, int animationIndex = 0)
    {
        var nodes = glb.Root.GetProperty("nodes");
        int meshNode = selectedNode ?? Enumerable.Range(0, nodes.GetArrayLength()).First(index => nodes[index].TryGetProperty("skin", out _));
        var skin = glb.Root.GetProperty("skins")[nodes[meshNode].GetProperty("skin").GetInt32()];
        var jointNodes = skin.GetProperty("joints").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        var inverse = glb.Accessor(skin.GetProperty("inverseBindMatrices").GetInt32()).Select(GlbReader.Matrix).ToArray();
        var primitive = glb.Root.GetProperty("meshes")[nodes[meshNode].GetProperty("mesh").GetInt32()].GetProperty("primitives")[partIndex];
        var attributes = primitive.GetProperty("attributes");
        var positions = glb.Accessor(attributes.GetProperty("POSITION").GetInt32());
        var bindPositions = positions.Select(GlbReader.Vector).ToArray();
        var mesh = glb.Root.GetProperty("meshes")[nodes[meshNode].GetProperty("mesh").GetInt32()];
        ApplyMorphWeights(glb, mesh, primitive, bindPositions);
        var influenceSets = new List<(float[][] Joints, float[][] Weights)>();
        for (int set = 0; attributes.TryGetProperty($"JOINTS_{set}", out var accessor); set++)
            influenceSets.Add((glb.Accessor(accessor.GetInt32()), glb.Accessor(attributes.GetProperty($"WEIGHTS_{set}").GetInt32())));
        var indices = glb.Accessor(primitive.GetProperty("indices").GetInt32());
        var sourceIndices = geometry.Parts.Where((part, index) => part.VertexIndices.Length > 0 &&
            (geometry.Particles == null || index < geometry.ParticlePart)).ElementAt(partIndex).VertexIndices;
        var sourceVertices = new Dictionary<int, int>();
        for (int corner = 0; corner < indices.Length; corner++)
            sourceVertices[(int)indices[corner][0]] = sourceIndices[corner];
        var sourceWeightSums = new float[geometry.Positions.Length];
        foreach (var binding in geometry.Skin)
            foreach (uint packed in binding.PackedWeights)
                sourceWeightSums[packed & 65535] += BitConverter.UInt32BitsToSingle(packed);
        foreach (float time in times)
        {
            actor.Loop = false; actor.Playing = false; actor.Time = time; actor.Evaluate(0);
            var expected = actor.Skin(source, geometry)!;
            if (!ReferenceEquals(expected, actor.Skin(source, geometry)))
                throw new InvalidOperationException("Actor skinning should reuse its per-geometry deformation buffer.");
            for (int vertex = 0; vertex < positions.Length; vertex++)
            {
                var actual = Vector3.Zero;
                foreach (var (joints, weights) in influenceSets)
                {
                    for (int component = 0; component < 4; component++)
                    {
                        int joint = (int)joints[vertex][component];
                        actual += Vector3.Transform(bindPositions[vertex], inverse[joint] * glb.World(jointNodes[joint], animationIndex, time)) * weights[vertex][component];
                    }
                }
                int sourceVertex = sourceVertices[vertex];
                // glTF requires normalized weights; Jade's packed vertex indices perturb their low mantissa bits.
                var expectedLocal = sourceWeightSums[sourceVertex] > 0 ? expected[sourceVertex] / sourceWeightSums[sourceVertex] : expected[sourceVertex];
                var expectedWorld = Geometry.ToRender(Vector3.Transform(expectedLocal, source.PreviewMatrix!.Value));
                Near(actual, Vector3.Transform(expectedWorld, placement ?? Matrix4x4.Identity), $"Skinned vertex {vertex} at {time}");
            }
        }
    }

    private static void ApplyMorphWeights(GlbReader glb, System.Text.Json.JsonElement mesh,
        System.Text.Json.JsonElement primitive, Vector3[] positions)
    {
        if (!primitive.TryGetProperty("targets", out var targets)) return;
        var weights = mesh.GetProperty("weights").EnumerateArray().Select(weight => weight.GetSingle()).ToArray();
        for (int group = 0; group < weights.Length; group++)
        {
            if (weights[group] == 0) continue;
            var deltas = glb.Accessor(targets[group].GetProperty("POSITION").GetInt32());
            for (int vertex = 0; vertex < positions.Length; vertex++)
                positions[vertex] += GlbReader.Vector(deltas[vertex]) * weights[group];
        }
    }

    internal static void VerifyActorMeshes(GlbReader glb, SceneActor actor, ResourceResolver resolver, float[] times)
    {
        var nodes = glb.Root.GetProperty("nodes");
        for (int nodeIndex = 0; nodeIndex < nodes.GetArrayLength(); nodeIndex++)
        {
            var node = nodes[nodeIndex];
            if (!node.TryGetProperty("skin", out _)) continue;
            uint key = Convert.ToUInt32(node.GetProperty("extras").GetProperty("gaoKey").GetString(), 16);
            var source = actor.Members.First(member => member.GaoKey == key);
            var geometry = resolver.Geometries[source.RenderGeometryKey];
            var primitives = glb.Root.GetProperty("meshes")[node.GetProperty("mesh").GetInt32()].GetProperty("primitives");
            for (int partIndex = 0; partIndex < primitives.GetArrayLength(); partIndex++)
                VerifySkin(glb, actor, source, geometry, times, selectedNode: nodeIndex, partIndex: partIndex);
        }
    }
    private static void Check(bool passed, string message) { if (!passed) throw new Exception(message); }
    private static void Near(Vector3 actual, Vector3 expected, string message) =>
        Check(Vector3.Distance(actual, expected) < .001f, $"{message}: {actual} != {expected}");
}
