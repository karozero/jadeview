using System.Numerics;

namespace JadeView;

public sealed partial class SceneActor
{
    private readonly Dictionary<Geometry, SkinningScratch> skinningScratch = [];

    private sealed class SkinningScratch(int vertexCount)
    {
        internal Vector3[] Positions { get; } = new Vector3[vertexCount];
        internal bool[] InfluencedVertices { get; } = new bool[vertexCount];
    }

    public Vector3[]? Skin(SceneObject sceneObject, Geometry geometry)
    {
        var sourcePositions = FaceMorphPreview.Get(sceneObject, geometry)?.Positions() ?? geometry.Positions;
        if (!CanSkin(sceneObject, geometry))
        {
            return ReferenceEquals(sourcePositions, geometry.Positions) ? null : sourcePositions;
        }

        if (!Matrix4x4.Invert(GetSceneObjectPose(sceneObject), out Matrix4x4 inverseMeshPose))
        {
            return null;
        }

        if (!skinningScratch.TryGetValue(geometry, out var scratch)
            || scratch.Positions.Length != geometry.Positions.Length)
            skinningScratch[geometry] = scratch = new(geometry.Positions.Length);
        var skinnedPositions = scratch.Positions;
        var influencedVertices = scratch.InfluencedVertices;
        Array.Clear(skinnedPositions);
        Array.Clear(influencedVertices);
        foreach (var binding in geometry.Skin)
        {
            if (!TryGetBonePose(sceneObject, binding.Bone, out Matrix4x4 bonePose))
            {
                continue;
            }

            AccumulateBindingWeights(binding, sourcePositions, skinnedPositions, influencedVertices,
                inverseMeshPose, bonePose, sceneObject.EffectiveDrawFlags);
        }

        RestoreUnweightedVertices(sourcePositions, skinnedPositions, influencedVertices);
        return skinnedPositions;
    }

    private static bool CanSkin(SceneObject sceneObject, Geometry geometry) =>
        geometry.Skin.Length > 0 && sceneObject.ActorNode != null && (sceneObject.Flags & 0x200000) != 0;

    private static Matrix4x4 GetSceneObjectPose(SceneObject sceneObject) =>
        sceneObject.PreviewMatrix ?? AnimationMath.Trs(sceneObject.Transform);

    private bool TryGetBonePose(SceneObject sceneObject, int boneIndex, out Matrix4x4 bonePose)
    {
        ActorNode actorNode = sceneObject.ActorNode!;
        if ((sceneObject.Flags & 0x1000000) != 0)
        {
            if (boneIndex < 0 || boneIndex >= actorNode.Links.Length)
            {
                bonePose = default;
                return false;
            }

            var link = actorNode.Links[boneIndex];
            SceneObject? target = ResolveBoneLink(link);
            if (target == null)
            {
                bonePose = default;
                return false;
            }

            bonePose = GetSceneObjectPose(target);
            return true;
        }

        if (boneIndex < 0 || boneIndex >= actorNode.Matrices.Length)
        {
            bonePose = default;
            return false;
        }

        bonePose = actorNode.Matrices[boneIndex] * GetSceneObjectPose(Root);
        return true;
    }

    private SceneObject? ResolveBoneLink(ActorBoneLink link)
    {
        if (link.Index == -1)
        {
            return Root;
        }

        if (link.Index >= 0 && link.Index < Bones.Count)
        {
            return Bones[link.Index];
        }

        return Members.FirstOrDefault(member => member.GaoKey == link.Key);
    }

    private static void AccumulateBindingWeights(SkinBinding binding, Vector3[] sourcePositions,
        Vector3[] skinnedPositions, bool[] influencedVertices, Matrix4x4 inverseMeshPose,
        Matrix4x4 bonePose, uint drawFlags)
    {
        var inverseBindPose = AnimationMath.SkinInverseBind(binding, drawFlags);
        var skinTransform = inverseBindPose * bonePose * inverseMeshPose;
        foreach (uint packedWeight in binding.PackedWeights)
        {
            int vertexIndex = (int)(packedWeight & 65535);
            if (vertexIndex >= skinnedPositions.Length)
            {
                continue;
            }

            float weight = BitConverter.UInt32BitsToSingle(packedWeight);
            skinnedPositions[vertexIndex] += Vector3.Transform(sourcePositions[vertexIndex], skinTransform) * weight;
            influencedVertices[vertexIndex] = true;
        }
    }

    private static void RestoreUnweightedVertices(Vector3[] sourcePositions,
        Vector3[] skinnedPositions, bool[] influencedVertices)
    {
        for (int vertexIndex = 0; vertexIndex < skinnedPositions.Length; vertexIndex++)
        {
            if (!influencedVertices[vertexIndex])
            {
                skinnedPositions[vertexIndex] = sourcePositions[vertexIndex];
            }
        }
    }
}
