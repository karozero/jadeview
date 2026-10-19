using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

public sealed partial class SceneExporter
{
    private int View(byte[] bytes, int target = 0)
    {
        while (binary.Position % GlbFormat.Alignment != 0) binary.WriteByte(0);
        var view = D("buffer", 0, "byteOffset", (int)binary.Position, "byteLength", bytes.Length);
        if (target != 0) view["target"] = target;
        views.Add(view);
        binary.Write(bytes);
        return views.Count - 1;
    }
    private int Floats(float[] values, int size, string type, bool bounds = false, int target = 0)
    {
        if (values.Any(value => !float.IsFinite(value))) throw new InvalidDataException("Non-finite values in exported geometry/animation.");
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        var accessor = D("bufferView", View(bytes, target), "componentType", GlbFormat.Float, "count", values.Length / size, "type", type);
        if (bounds)
        {
            accessor["min"] = ComponentBounds(values, size, minimum: true);
            accessor["max"] = ComponentBounds(values, size, minimum: false);
        }
        accessors.Add(accessor);
        return accessors.Count - 1;
    }
    private static float[] ComponentBounds(float[] values, int size, bool minimum) =>
        Enumerable.Range(0, size).Select(component =>
        {
            var samples = Enumerable.Range(0, values.Length / size).Select(element => values[element * size + component]);
            return minimum ? samples.Min() : samples.Max();
        }).ToArray();

    private int Ushorts(ushort[] values)
    {
        var bytes = new byte[values.Length * 2];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        accessors.Add(D("bufferView", View(bytes, GlbFormat.VertexBuffer), "componentType", GlbFormat.UnsignedShort, "count", values.Length / GlbFormat.InfluencesPerSet, "type", "VEC4"));
        return accessors.Count - 1;
    }
    private int Indices(uint[] indices)
    {
        bool useShort = indices.Max() < ushort.MaxValue;
        Array values = useShort ? indices.Select(index => checked((ushort)index)).ToArray() : indices;
        var bytes = new byte[Buffer.ByteLength(values)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        accessors.Add(D("bufferView", View(bytes, GlbFormat.IndexBuffer), "componentType", useShort ? GlbFormat.UnsignedShort : GlbFormat.UnsignedInt,
            "count", indices.Length, "type", "SCALAR"));
        return accessors.Count - 1;
    }
}
