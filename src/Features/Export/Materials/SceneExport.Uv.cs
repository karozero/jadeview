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
        if ((layer.Flags & JadeMaterialFlags.Scrolling) != 0)
            warnings.Add($"Animated texture UV transforms are sampled at {ExportMaterialTime:G6}s; UV motion is not exported.");
        var matrix = layer.UvMatrix(ExportMaterialTime, out var shift);
        var transformed = new Vector2(matrix.X * uv.X + matrix.Y * uv.Y + shift.X,
            matrix.Z * uv.X + matrix.W * uv.Y + shift.Y);
        // DecodeTga writes top-down PNGs; Jade source V has the opposite origin.
        return new(transformed.X, 1 - transformed.Y);
    }

    private float ExportMaterialTime => options.CinematicMaterialLayers is { Time: var time }
        && float.IsFinite(time) ? Math.Max(0, time) : 0;

    private Vector2 PlanarUv(SceneObject sceneObject, Vector3 point, JadeMaterialLayer layer)
    {
        int space = layer.Custom >> 6 & 3;
        if (space == 3 && TryBoneProjection(sceneObject, layer, out var boneProjection))
            point = Vector3.Transform(point, boneProjection);
        else if (space != 0)
        {
            if (space >= 2)
                warnings.Add("Unresolved view/bone planar UV is approximated with world projection.");
            var sourceMatrix = options.Placement == ExportPlacement.Original ? sceneObject.InitialTransform.ToRenderMatrix() : sceneObject.RenderMatrix;
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

    private static bool TryBoneProjection(SceneObject sceneObject, JadeMaterialLayer layer, out Matrix4x4 projection)
    {
        projection = Matrix4x4.Identity;
        var palette = sceneObject.ActorNode;
        if (palette == null) return false;
        int index = layer.Custom >> 8 & 7;
        if ((sceneObject.Flags & ExportObjectFlags.LinkedPalette) == 0)
            return index < palette.Matrices.Length && Matrix4x4.Invert(palette.Matrices[index], out projection);
        if (index >= palette.Links.Length || sceneObject.Actor is not { } actor) return false;
        var link = palette.Links[index];
        var bone = ResolvePaletteBone(actor, link.Index, link.Key);
        var meshWorld = sceneObject.PreviewMatrix ?? AnimationMath.Trs(sceneObject.Transform);
        if (bone == null || !Matrix4x4.Invert(meshWorld, out var inverseMesh)) return false;
        var boneWorld = bone.PreviewMatrix ?? AnimationMath.Trs(bone.Transform);
        return Matrix4x4.Invert(boneWorld * inverseMesh, out projection);
    }
}
