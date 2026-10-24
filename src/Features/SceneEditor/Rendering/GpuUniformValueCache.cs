using System.Runtime.InteropServices;

namespace JadeView;

// Shader uniforms persist across program binds. Compare the exact uploaded bits,
// including signed zero and NaN payloads, rather than numeric approximate equality.
internal sealed class GpuUniformValueCache<T> where T : unmanaged
{
    private readonly Dictionary<int, T> values = [];

    internal bool Changed(int location, T value)
    {
        if (location < 0) return false;
        if (values.TryGetValue(location, out var previous)
            && MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref previous, 1))
                .SequenceEqual(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1))))
            return false;
        values[location] = value;
        return true;
    }

    internal void Clear() => values.Clear();
}
