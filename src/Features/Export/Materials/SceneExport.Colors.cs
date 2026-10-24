using System.Numerics;
using Obj = System.Collections.Generic.Dictionary<string, object>;

namespace JadeView;

public sealed partial class SceneExporter
{
    private void WriteVertexColors(Obj attributes, SceneObject sceneObject, Geometry geometry, List<int> sourceVertices,
        JadeMaterialLayer? layer, Geometry.Part part)
    {
        if (layer?.ColorMode is not (0 or 3 or 5 or 6) || sourceVertices.Count == 0) return;
        bool computed = layer.ColorMode is 0 or 6;
        bool spritePart = part.VertexIndices.Length > 0 && geometry.IsBillboardVertex(part.VertexIndices[0]);
        if (computed && ComputedExportColor(sceneObject, geometry, sourceVertices[0], layer, spritePart) == null) return;
        bool invertRgb = layer.ColorMode == 5;
        bool overrideAlpha = (layer.Flags & JadeMaterialFlags.OverrideAlpha) != 0;
        var colors = new List<float>(sourceVertices.Count * 4);
        foreach (int vertex in sourceVertices)
        {
            // Match the renderer's absent-paint value; do not silently substitute white.
            uint packed = GeometryVertexColor.PackedColor(sceneObject, geometry, vertex);
            Vector4 colorValue = computed
                ? ComputedExportColor(sceneObject, geometry, vertex, layer, spritePart)!.Value
                : new Vector4(packed & 255, packed >> 8 & 255, packed >> 16 & 255, packed >> 24) / 255f;
            for (int channel = 0; channel < 3; channel++)
            {
                float color = colorValue[channel];
                colors.Add(invertRgb ? 1 - color : color);
            }
            // The override is applied once, through baseColorFactor.a.
            colors.Add(overrideAlpha ? 1 : colorValue.W);
        }
        attributes["COLOR_0"] = Floats(colors.ToArray(), 4, "VEC4", false, 34962);
    }
}
