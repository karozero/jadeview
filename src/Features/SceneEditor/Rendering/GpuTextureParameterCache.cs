namespace JadeView;

/// <summary>
/// Texture parameters belong to the GL texture object and remain set across draws.
/// This cache suppresses identical requests until the renderer unloads its textures.
/// </summary>
internal sealed class GpuTextureParameterCache
{
    private readonly Dictionary<(uint TextureId, int Parameter), int> values = [];

    internal bool Record(uint textureId, int parameter, int value)
    {
        var key = (textureId, parameter);
        if (values.TryGetValue(key, out int previous) && previous == value)
            return false;
        values[key] = value;
        return true;
    }

    internal void Clear() => values.Clear();
}
