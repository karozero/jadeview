using System.Numerics;

namespace JadeView;

public sealed partial class SceneExporter
{
    private static Vector3[] ReferenceReflectionNormals(SceneObject sceneObject, Geometry geometry, Vector3[] positions)
    {
        var normals = AnimationMath.Normals(positions, geometry.Parts.ToList());
        var relative = Matrix4x4.Identity;
        if (sceneObject.Actor is { } actor)
        {
            var rootPose = actor.Root.PreviewMatrix ?? AnimationMath.Trs(actor.Root.Transform);
            var meshPose = sceneObject.PreviewMatrix ?? AnimationMath.Trs(sceneObject.Transform);
            if (Matrix4x4.Invert(rootPose, out var inverseRoot)) relative = meshPose * inverseRoot;
        }
        if (!Matrix4x4.Invert(relative, out var inverse)) return normals;
        var normalMatrix = Matrix4x4.Transpose(inverse);
        return normals.Select(normal =>
        {
            var transformed = Vector3.TransformNormal(normal, normalMatrix);
            return transformed.LengthSquared() > 1e-15f ? Vector3.Normalize(transformed) : Vector3.UnitZ;
        }).ToArray();
    }

    private static Vector2 ReferenceReflectionUv(Vector3 normal, JadeMaterialLayer layer, uint drawFlags)
    {
        // A fixed frontal actor-space view. Translation and scene placement must
        // not shift the reflection. This is a static approximation of uvMode 2.
        var projected = new Vector2(normal.X, normal.Z) * .5f;
        if ((drawFlags & 0x8000) != 0) projected.X = -projected.X;
        var matrix = layer.UvMatrix(0, out var shift);
        var transformed = new Vector2(matrix.X * projected.X + matrix.Y * projected.Y + shift.X,
            matrix.Z * projected.X + matrix.W * projected.Y + shift.Y);
        return new Vector2(.5f) - transformed;
    }

    private static Vector4[][] ReflectionCornerTints(SceneObject sceneObject, Geometry geometry, Geometry.Part part,
        ResolvedMaterial? material, IReadOnlyList<JadeMaterialLayer> layers, int triangle) =>
        layers.Select(layer => Enumerable.Range(triangle * 3, 3).Select(corner =>
            ReflectionTint(sceneObject, geometry, part.VertexIndices[corner], material, layer)).ToArray()).ToArray();

    private static Vector4 ReflectionTint(SceneObject sceneObject, Geometry geometry, int vertex, ResolvedMaterial? material, JadeMaterialLayer layer)
    {
        var tint = Vector4.One;
        if (layer.ColorMode == 2 && material?.SourceBytes is { Length: >= 16 } bytes)
        {
            uint packed = Preview.U32(bytes, 12);
            tint = new Vector4(packed & 255, packed >> 8 & 255, packed >> 16 & 255, 0) / 255;
        }
        bool hasInstancePaint = vertex < sceneObject.RliColors.Length;
        uint paint = hasInstancePaint ? sceneObject.RliColors[vertex]
            : vertex < geometry.VertexColors.Length ? geometry.VertexColors[vertex] : 0;
        bool usesInstanceColor = sceneObject.RliColors.Length > 0 && layer.Blend is not (0 or 7)
            && layer.ColorMode is 0 or 6;
        if (layer.ColorMode is 3 or 5 || usesInstanceColor)
        {
            tint = new Vector4(paint & 255, paint >> 8 & 255, paint >> 16 & 255, paint >> 24) / 255;
            if (layer.ColorMode == 5)
            {
                tint.X = 1 - tint.X; tint.Y = 1 - tint.Y; tint.Z = 1 - tint.Z;
            }
        }
        bool usesInstanceAlpha = sceneObject.RliColors.Length > 0 && layer.Blend != 0
            && layer.ColorMode is 0 or 6;
        if (usesInstanceAlpha) tint.W = hasInstancePaint ? (paint >> 24) / 255f : 1;
        if ((layer.Flags & JadeMaterialFlags.OverrideAlpha) != 0) tint.W = layer.OverrideAlpha;
        return tint;
    }
}
