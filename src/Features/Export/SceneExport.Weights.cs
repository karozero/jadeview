using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

public sealed partial class SceneExporter
{
    private static List<(int Joint, float Weight)>[] BuildInfluences(int vertexCount, SkinBinding[]? bindings)
    {
        var influences = Enumerable.Range(0, vertexCount).Select(_ => new List<(int Joint, float Weight)>()).ToArray();
        if (bindings == null)
            return influences;
        for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
        {
            foreach (uint packed in bindings[bindingIndex].PackedWeights)
            {
                int vertex = (int)(packed & 65535);
                float weight = BitConverter.UInt32BitsToSingle(packed);
                if (vertex < vertexCount && float.IsFinite(weight) && weight > 0)
                    influences[vertex].Add((bindingIndex, weight));
            }
        }
        foreach (var weights in influences)
        {
            if (weights.Count == 0)
                weights.Add((bindings.Length, 1));
            float total = weights.Sum(weight => weight.Weight);
            if (!float.IsFinite(total) || total <= 0)
                throw new InvalidDataException("Invalid total skin weight in exported geometry.");
            for (int index = 0; index < weights.Count; index++)
                weights[index] = (weights[index].Joint, weights[index].Weight / total);
        }
        return influences;
    }

    private void WriteInfluences(Obj attributes, List<int> vertices, List<(int Joint, float Weight)>[] influences)
    {
        int sets = (vertices.Max(vertex => influences[vertex].Count) + 3) / 4;
        for (int set = 0; set < sets; set++)
        {
            var joints = new List<ushort>();
            var weights = new List<float>();
            foreach (int vertex in vertices)
            {
                for (int component = 0; component < 4; component++)
                {
                    int index = set * 4 + component;
                    var influence = index < influences[vertex].Count ? influences[vertex][index] : (Joint: 0, Weight: 0f);
                    joints.Add(checked((ushort)influence.Joint));
                    weights.Add(influence.Weight);
                }
            }
            attributes[$"JOINTS_{set}"] = Ushorts(joints.ToArray());
            attributes[$"WEIGHTS_{set}"] = Floats(weights.ToArray(), 4, "VEC4", false, 34962);
        }
    }
}
