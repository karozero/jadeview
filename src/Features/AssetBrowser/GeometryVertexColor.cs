namespace JadeView;

internal static class GeometryVertexColor
{
    internal static uint PackedColor(SceneObject owner, Geometry geometry, int vertex) =>
        vertex < owner.RliColors.Length ? owner.RliColors[vertex]
            : vertex < geometry.VertexColors.Length ? geometry.VertexColors[vertex] : 0;

    internal static float InstanceAlpha(SceneObject owner, Geometry geometry, int vertex, uint packed) =>
        vertex < owner.RliColors.Length || geometry.IsBillboardVertex(vertex) ? (packed >> 24) / 255f : 1;
}
