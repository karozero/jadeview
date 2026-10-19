using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

public sealed partial class SceneExporter
{
    private void WriteVertexColors(Obj attributes, Geometry geometry, List<int> sourceVertices,
        JadeMaterialLayer? layer)
    {
        if (layer?.ColorMode is not (3 or 5)) return;
        bool invertRgb = layer.ColorMode == 5;
        bool overrideAlpha = (layer.Flags & 0x400) != 0;
        var colors = new List<float>(sourceVertices.Count * 4);
        foreach (int vertex in sourceVertices)
        {
            // Match the renderer's absent-paint value; do not silently substitute white.
            uint packed = vertex < geometry.VertexColors.Length ? geometry.VertexColors[vertex] : 0;
            for (int channel = 0; channel < 3; channel++)
            {
                float color = (packed >> (channel * 8) & 255) / 255f;
                colors.Add(invertRgb ? 1 - color : color);
            }
            // The override is applied once, through baseColorFactor.a.
            colors.Add(overrideAlpha ? 1 : (packed >> 24) / 255f);
        }
        attributes["COLOR_0"] = Floats(colors.ToArray(), 4, "VEC4", false, 34962);
    }
}
