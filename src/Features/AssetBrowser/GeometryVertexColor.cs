namespace JadeView;

internal static class GeometryVertexColor
{
    internal static float InstanceAlpha(SceneObject owner, Geometry geometry, int vertex, uint packed) =>
        vertex < owner.RliColors.Length || geometry.IsBillboardVertex(vertex) ? (packed >> 24) / 255f : 1;
}
