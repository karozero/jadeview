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
            MaterialLayerTint.Evaluate(sceneObject, geometry, part.VertexIndices[corner], material, layer)).ToArray()).ToArray();

}
