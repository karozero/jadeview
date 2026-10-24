using Raylib_cs;

namespace JadeView;

public sealed partial class SceneRenderer
{
    private readonly GpuTextureParameterCache textureParameterCache = new();

    internal bool CacheTextureParameterValues { get; set; } = true;
    public int StatTextureParameterRequests { get; private set; }
    public int StatTextureParameterCalls { get; private set; }
    public int StatTextureParameterCacheHits { get; private set; }

    private void SetTextureParameter(Texture2D texture, int parameter, int value)
    {
        StatTextureParameterRequests++;
        bool changed = textureParameterCache.Record(texture.Id, parameter, value);
        if (CacheTextureParameterValues && !changed)
        {
            StatTextureParameterCacheHits++;
            return;
        }

        StatTextureParameterCalls++;
        Rlgl.TextureParameters(texture.Id, parameter, value);
    }

    private void ResetTextureParameterStatistics()
    {
        StatTextureParameterRequests = 0;
        StatTextureParameterCalls = 0;
        StatTextureParameterCacheHits = 0;
    }
}
