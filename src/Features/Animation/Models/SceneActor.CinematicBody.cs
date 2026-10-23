using System.Numerics;

namespace JadeView;

public sealed partial class SceneActor
{
    private CinematicBodyPoseSampler? bodyPoseSampler;
    internal int CinematicBodyBaselineRefreshCount => bodyPoseSampler?.BaselineRefreshCount ?? 0;

    internal void CompensateBodyRoot(SceneObject target, AnimationKey key, Matrix4x4? rootInverse,
        ref Vector3 position, ref Quaternion rotation)
    {
        if (rootInverse is not { } reference || target == Root || PoseParent(target) != Root) return;
        // Native EVE_Event_InterpolationKey_Play converts direct child bones
        // from the animation's Flash frame into its moving MagicBox frame.
        if (key.Position != null) position = Vector3.Transform(position, reference);
        if (key.Rotation != null && Matrix4x4.Decompose(
            Matrix4x4.CreateFromQuaternion(rotation) * reference, out _, out var compensated, out _))
            rotation = compensated;
    }

    internal SceneObject PoseParent(SceneObject target)
    {
        uint parentKey = target.ActorNode?.Parent ?? uint.MaxValue;
        foreach (var member in Members)
            if (member != target && member.GaoKey == parentKey) return member;
        return Root;
    }
}
