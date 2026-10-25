using System.Numerics;

namespace JadeView;

// Evaluate tint for static reflection/detail baking, matching renderer color modes.
internal static class MaterialLayerTint
{
    internal static Vector4 Evaluate(SceneObject sceneObject, Geometry geometry, int vertex, ResolvedMaterial? material,
        JadeMaterialLayer layer, Vector4? computedDiffuse = null, Vector4? materialConstant = null)
    {
        var tint = Vector4.One;
        if (layer.ColorMode == 2) tint = materialConstant ?? ReadConstant(material);
        uint paint = GeometryVertexColor.PackedColor(sceneObject, geometry, vertex);
        bool usesInstanceColor = (geometry.IsBillboardVertex(vertex)
            || sceneObject.RliColors.Length > 0 && layer.Blend is not (0 or 7))
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
        bool usesInstanceAlpha = (sceneObject.RliColors.Length > 0 || geometry.IsBillboardVertex(vertex)) && layer.Blend != 0
            && layer.ColorMode is 0 or 6;
        if (computedDiffuse is Vector4 computed && layer.ColorMode is 0 or 6) tint = computed;
        if (usesInstanceAlpha && computedDiffuse == null)
            tint.W = GeometryVertexColor.InstanceAlpha(sceneObject, geometry, vertex, paint);
        if ((layer.Flags & JadeMaterialFlags.OverrideAlpha) != 0) tint.W = layer.OverrideAlpha;
        return tint;
    }

    internal static Vector4 ReadConstant(ResolvedMaterial? material)
    {
        if (material?.Specular is not uint packed) return Vector4.One;
        // Jade's ColorOp_Disable path uses ul_Specular as its constant RGB.
        // This is independent of ul_Diffuse, and the packed alpha byte is ignored.
        return new Vector4(packed & 255, packed >> 8 & 255, packed >> 16 & 255, 255) / 255f;
    }
}
