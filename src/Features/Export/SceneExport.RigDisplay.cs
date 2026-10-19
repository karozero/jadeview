using System.Numerics;

namespace JadeView;

public sealed partial class SceneExporter
{
    private readonly Dictionary<SceneActor, (int Node, Matrix4x4 InverseBind)> rootDisplayTips = [];

    private void AddRootDisplayTip(SceneActor actor, Dictionary<SceneObject, int> nodeMap,
        Dictionary<SceneObject, Matrix4x4> bindPose)
    {
        // glTF has joint positions, but no bone lengths. If all root children
        // coincide with it, Blender uses the actor's scene translation as its
        // displayed length. A short unweighted child gives it a local endpoint.
        var rootBind = AnimationMath.ToRender(bindPose[actor.Root]);
        if (!Matrix4x4.Invert(rootBind, out var inverseRoot)) return;
        var rootChildren = actor.Members.Where(member => IsDirectRootChild(actor, member));
        bool hasOffsetChild = rootChildren.Any(member =>
            (AnimationMath.ToRender(bindPose[member]) * inverseRoot).Translation.Length() > .004f);
        if (hasOffsetChild) return;
        var local = Matrix4x4.CreateTranslation(0, .05f, 0);
        if (!Matrix4x4.Invert(local * rootBind, out var inverseTip)) return;
        int tip = nodes.Count;
        nodes.Add(D("name", actor.Root.Name + " display tip", "translation", new[] { 0f, .05f, 0f },
            "extras", D("displayOnly", true, "sourceJoint", false)));
        AddChild(nodeMap[actor.Root], tip);
        rootDisplayTips[actor] = (tip, inverseTip);
    }

    private static bool IsDirectRootChild(SceneActor actor, SceneObject member)
    {
        if (member == actor.Root) return false;
        bool explicitRoot = member.ActorNode?.Parent == actor.Root.GaoKey;
        bool hasOtherParent = actor.Members.Any(parent => parent != member && parent.GaoKey == member.ActorNode?.Parent);
        return explicitRoot || !hasOtherParent;
    }
}
