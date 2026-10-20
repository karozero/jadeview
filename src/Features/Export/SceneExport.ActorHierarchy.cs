using System.Numerics;

namespace JadeView;

public sealed partial class SceneExporter
{
    private List<(int Node, Func<Matrix4x4> Pose)> ActorHierarchy(SceneActor actor, int group,
        Dictionary<SceneObject, int> nodeMap)
    {
        var animated = new List<(int Node, Func<Matrix4x4> Pose)>();
        foreach (var member in actor.Members)
        {
            var parent = AnimationParent(actor, member);
            AddChild(parent == null ? group : nodeMap[parent], nodeMap[member]);
            Matrix4x4 Pose() => RelativeAnimationPose(member, parent);
            SetTrs(nodes[nodeMap[member]], Pose());
            animated.Add((nodeMap[member], Pose));
        }
        return animated;
    }

    private static SceneObject? AnimationParent(SceneActor actor, SceneObject member)
    {
        if (member == actor.Root) return null;
        return actor.Members.FirstOrDefault(candidate =>
            candidate != member && candidate.GaoKey == member.ActorNode?.Parent) ?? actor.Root;
    }

    private static Matrix4x4 RelativeAnimationPose(SceneObject member, SceneObject? parent)
    {
        if (parent == null) return member.RenderMatrix;
        if (!Matrix4x4.Invert(parent.RenderMatrix, out var inverse))
        {
            // Zero scale is an authored animation state. World matrices lose the
            // child's local pose there; retain the evaluated pose before composition.
            if (member.Actor is { } actor && actor.PoseParent(member) == parent
                && actor.TryGetEvaluatedLocal(member, out var local))
                return AnimationMath.ToRender(local);
            throw new InvalidDataException($"Singular animation parent transform for '{member.Name}'.");
        }
        return member.RenderMatrix * inverse;
    }
}
