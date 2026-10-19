using System.Numerics;

namespace JadeView;

// Evaluate tint for static reflection/detail baking, matching renderer color modes.
internal static class MaterialLayerTint
{
    internal static Vector4 Evaluate(SceneObject sceneObject, Geometry geometry, int vertex, ResolvedMaterial? material, JadeMaterialLayer layer)
    {
        var tint = Vector4.One;
        if (layer.ColorMode == 2 && material?.SourceBytes is { Length: >= 16 } bytes)
        {
            uint packed = Preview.U32(bytes, 12);
            tint = new Vector4(packed & 255, packed >> 8 & 255, packed >> 16 & 255, 0) / 255;
        }
        bool hasInstancePaint = vertex < sceneObject.RliColors.Length;
        uint paint = VertexPaint(sceneObject, geometry, vertex);
        bool usesInstanceColor = sceneObject.RliColors.Length > 0 && layer.Blend is not (0 or 7)
            && layer.ColorMode is 0 or 6;
        if (layer.ColorMode is 3 or 5 || usesInstanceColor)
        {
            tint = new Vector4(paint & 255, paint >> 8 & 255, paint >> 16 & 255, paint >> 24) / 255;
            if (layer.ColorMode == 5)
            {
                tint.X = 1 - tint.X;
                tint.Y = 1 - tint.Y;
                tint.Z = 1 - tint.Z;
            }
        }
        bool usesInstanceAlpha = sceneObject.RliColors.Length > 0 && layer.Blend != 0
            && layer.ColorMode is 0 or 6;
        if (usesInstanceAlpha) tint.W = hasInstancePaint ? (paint >> 24) / 255f : 1;
        if ((layer.Flags & JadeMaterialFlags.OverrideAlpha) != 0) tint.W = layer.OverrideAlpha;
        return tint;
    }
    private static uint VertexPaint(SceneObject sceneObject, Geometry geometry, int vertex)
    {
        if (vertex < sceneObject.RliColors.Length) return sceneObject.RliColors[vertex];
        return vertex < geometry.VertexColors.Length ? geometry.VertexColors[vertex] : 0;
    }
}
