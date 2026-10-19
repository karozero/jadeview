using System.Numerics;
using System.Text.Json;

internal sealed class GlbReader : IDisposable
{
    private readonly JsonDocument document;
    private readonly byte[] binary;
    private readonly Dictionary<int, float[][]> accessorCache = [];
    private readonly Dictionary<(int Node, int Animation, float Time), Matrix4x4> worldCache = [];
    internal JsonElement Root => document.RootElement;
    internal byte[] Image(int index)
    {
        var image = Root.GetProperty("images")[index];
        var view = Root.GetProperty("bufferViews")[image.GetProperty("bufferView").GetInt32()];
        int offset = view.GetProperty("byteOffset").GetInt32();
        return binary.AsSpan(offset, view.GetProperty("byteLength").GetInt32()).ToArray();
    }

    internal GlbReader(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.ReadUInt32() != 0x46546C67 || reader.ReadUInt32() != 2 || reader.ReadUInt32() != reader.BaseStream.Length)
            throw new Exception("Invalid GLB header");
        int length = reader.ReadInt32();
        if (reader.ReadUInt32() != 0x4E4F534A) throw new Exception("Missing GLB JSON");
        document = JsonDocument.Parse(reader.ReadBytes(length));
        length = reader.ReadInt32();
        if (reader.ReadUInt32() != 0x004E4942) throw new Exception("Missing GLB binary");
        binary = reader.ReadBytes(length);
    }

    internal float[][] Accessor(int index)
    {
        if (accessorCache.TryGetValue(index, out var cached)) return cached;
        var accessor = Root.GetProperty("accessors")[index];
        var view = Root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
        int offset = view.GetProperty("byteOffset").GetInt32();
        int components = accessor.GetProperty("type").GetString() switch
        {
            "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => 1
        };
        int componentType = accessor.GetProperty("componentType").GetInt32();
        int size = componentType == 5123 ? 2 : 4;
        var values = Enumerable.Range(0, accessor.GetProperty("count").GetInt32()).Select(element =>
            Enumerable.Range(0, components).Select(component =>
            {
                int position = offset + (element * components + component) * size;
                return componentType switch
                {
                    5123 => BitConverter.ToUInt16(binary, position),
                    5125 => BitConverter.ToUInt32(binary, position),
                    _ => BitConverter.ToSingle(binary, position)
                };
            }).ToArray()).ToArray();
        accessorCache.Add(index, values);
        return values;
    }

    internal Matrix4x4 World(int nodeIndex, int animationIndex = -1, float time = 0)
    {
        var key = (nodeIndex, animationIndex, time);
        if (worldCache.TryGetValue(key, out var cached)) return cached;
        var result = ReadWorld(nodeIndex, animationIndex, time);
        worldCache.Add(key, result);
        return result;
    }

    private Matrix4x4 ReadWorld(int nodeIndex, int animationIndex, float time)
    {
        var node = Root.GetProperty("nodes")[nodeIndex];
        Matrix4x4 local;
        if (node.TryGetProperty("matrix", out var matrix))
            local = Matrix(matrix.EnumerateArray().Select(value => value.GetSingle()).ToArray());
        else
        {
            var translation = Property(node, "translation", [0, 0, 0]);
            var rotation = Property(node, "rotation", [0, 0, 0, 1]);
            var scale = Property(node, "scale", [1, 1, 1]);
            if (animationIndex >= 0)
            {
                var animation = Root.GetProperty("animations")[animationIndex];
                foreach (var channel in animation.GetProperty("channels").EnumerateArray())
                {
                    var target = channel.GetProperty("target");
                    if (target.GetProperty("node").GetInt32() != nodeIndex) continue;
                    var sampler = animation.GetProperty("samplers")[channel.GetProperty("sampler").GetInt32()];
                    var times = Accessor(sampler.GetProperty("input").GetInt32()).Select(value => value[0]).ToArray();
                    int frame = Array.FindIndex(times, value => value == time);
                    if (frame < 0) throw new Exception($"Requested time {time} is missing");
                    var value = Accessor(sampler.GetProperty("output").GetInt32())[frame];
                    switch (target.GetProperty("path").GetString())
                    {
                        case "translation": translation = value; break;
                        case "rotation": rotation = value; break;
                        case "scale": scale = value; break;
                    }
                }
            }
            local = Matrix4x4.CreateScale(Vector(scale)) * Matrix4x4.CreateFromQuaternion(
                new(rotation[0], rotation[1], rotation[2], rotation[3])) * Matrix4x4.CreateTranslation(Vector(translation));
        }
        var nodes = Root.GetProperty("nodes");
        for (int parent = 0; parent < nodes.GetArrayLength(); parent++)
        {
            if (nodes[parent].TryGetProperty("children", out var children) &&
                children.EnumerateArray().Any(child => child.GetInt32() == nodeIndex))
                return local * World(parent, animationIndex, time);
        }
        return local;
    }

    private static float[] Property(JsonElement node, string name, float[] fallback) =>
        node.TryGetProperty(name, out var value) ? value.EnumerateArray().Select(item => item.GetSingle()).ToArray() : fallback;
    internal static Vector3 Vector(float[] values) => new(values[0], values[1], values[2]);
    internal static Matrix4x4 Matrix(float[] values) => new(values[0], values[1], values[2], values[3],
        values[4], values[5], values[6], values[7], values[8], values[9], values[10], values[11],
        values[12], values[13], values[14], values[15]);
    public void Dispose() => document.Dispose();
}
