using System.Numerics;

namespace JadeView;

public sealed partial class SceneExporter
{
    private Vector2 ExportUv(SceneObject sceneObject, Vector3 position, Vector2 uv, JadeMaterialLayer? layer)
    {
        if (layer == null)
            return new(uv.X, 1 - uv.Y);
        if (layer.UvMode == 6)
            uv = PlanarUv(sceneObject, position, layer);
        if (layer.UvMode is 2 or 3)
            warnings.Add("View-dependent environment/diffuse UV uses the stored UV map in the export.");
        if ((layer.Flags & 0xC0000000) != 0)
            warnings.Add("Animated texture UV transforms are frozen at time zero.");
        var matrix = layer.UvMatrix(0, out var shift);
        var transformed = new Vector2(matrix.X * uv.X + matrix.Y * uv.Y + shift.X,
            matrix.Z * uv.X + matrix.W * uv.Y + shift.Y);
        // DecodeTga writes top-down PNGs; Jade source V has the opposite origin.
        return new(transformed.X, 1 - transformed.Y);
    }

    private Vector2 PlanarUv(SceneObject sceneObject, Vector3 point, JadeMaterialLayer layer)
    {
        int space = layer.Custom >> 6 & 3;
        if (space >= 2)
            warnings.Add("View/bone planar UV is approximated with world projection.");
        if (space != 0)
        {
            var sourceMatrix = options.Placement == 1 ? sceneObject.InitialTransform.ToRenderMatrix() : sceneObject.RenderMatrix;
            point = TransformData.FromRender(Vector3.Transform(Geometry.ToRender(point), sourceMatrix));
        }
        int axis = layer.Custom >> 4 & 3;
        return axis switch
        {
            0 => new(point.Y, point.Z),
            1 => new(point.X, point.Z),
            2 => new(point.X, point.Y),
            _ => new(Vector3.Dot(point, new(.70711f, -.70711f, 0)),
                Vector3.Dot(point, new(-.57735f, -.57735f, .57735f)))
        };
    }
}
