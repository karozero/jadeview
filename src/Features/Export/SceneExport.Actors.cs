using System.Numerics;

namespace JadeView;

public sealed partial class SceneExporter
{
    private void Actor(SceneActor actor, List<SceneObject> objects)
    {
        using var snapshot = new ActorExportSnapshot(actor);
        actor.Playing = false;
        actor.Loop = false;
        actor.Evaluate(0);
        var children = new List<int>();
        int group = nodes.Count;
        nodes.Add(D("name", actor.Root.Name, "children", children,
            "extras", D("actor", true, "gaoKey", actor.Root.GaoKey.ToString("X8"))));
        roots.Add(group);
        var offset = ActorPlacement(actor);
        if (!options.Animations)
        {
            foreach (var sceneObject in objects)
                Static(sceneObject, children, sceneObject.RenderMatrix * offset);
            return;
        }

        nodes[group]["matrix"] = Matrix(offset);
        var bindPose = ReadBindPose(actor);
        var nodeMap = actor.Members.ToDictionary(member => member, member => Node(member, Matrix4x4.Identity, true));
        var animated = ActorHierarchy(actor, group, nodeMap);
        AddRootDisplayTip(actor, nodeMap, bindPose);
        foreach (var sceneObject in objects.Where(Included))
            ActorMesh(actor, sceneObject, group, nodeMap, bindPose);
        WriteAnimations(actor, animated);
        WriteMorphAnimations(actor);
    }

    private Matrix4x4 ActorPlacement(SceneActor actor)
    {
        if (options.Placement == ExportPlacement.AtOrigin) return originOffset;
        if (options.Placement != 1) return Matrix4x4.Identity;
        if (!Matrix4x4.Invert(actor.Root.RenderMatrix, out var inverse))
            throw new InvalidDataException("Singular actor placement transform.");
        return inverse * actor.Root.InitialTransform.ToRenderMatrix();
    }

    private static Dictionary<SceneObject, Matrix4x4> ReadBindPose(SceneActor actor)
    {
        var clip = actor.Clip;
        actor.Clip = null;
        actor.Evaluate(0);
        var pose = actor.Members.ToDictionary(member => member,
            member => member.PreviewMatrix ?? AnimationMath.Trs(member.Transform));
        actor.Clip = clip;
        actor.Evaluate(0);
        return pose;
    }

    private List<(int Node, Func<Matrix4x4> Pose)> ActorHierarchy(SceneActor actor, int group,
        Dictionary<SceneObject, int> nodeMap)
    {
        var animated = new List<(int Node, Func<Matrix4x4> Pose)>();
        foreach (var member in actor.Members)
        {
            var parent = member == actor.Root ? null : actor.Members.FirstOrDefault(candidate =>
                candidate != member && candidate.GaoKey == member.ActorNode?.Parent) ?? actor.Root;
            AddChild(parent == null ? group : nodeMap[parent], nodeMap[member]);
            Matrix4x4 Pose()
            {
                if (parent == null) return member.RenderMatrix;
                if (!Matrix4x4.Invert(parent.RenderMatrix, out var inverse))
                    throw new InvalidDataException("Singular animation parent transform.");
                return member.RenderMatrix * inverse;
            }
            SetTrs(nodes[nodeMap[member]], Pose());
            animated.Add((nodeMap[member], Pose));
        }
        return animated;
    }

    private void ActorMesh(SceneActor actor, SceneObject sceneObject, int group,
        Dictionary<SceneObject, int> nodeMap, Dictionary<SceneObject, Matrix4x4> bindPose)
    {
        if (!resolver.Geometries.TryGetValue(sceneObject.RenderGeometryKey, out var geometry))
        {
            if (options.UnknownObjects) empties++;
            return;
        }
        var bindings = SkinBindings(actor, sceneObject, geometry, nodeMap);
        if (bindings.Count == 0)
        {
            nodes[nodeMap[sceneObject]]["mesh"] = Mesh(sceneObject, geometry,
                actor.Skin(sceneObject, geometry) ?? geometry.Positions, null);
            return;
        }
        var meshBind = bindPose[sceneObject];
        if (!Matrix4x4.Invert(AnimationMath.ToRender(meshBind), out var inverseMeshBind))
            throw new InvalidDataException($"Singular mesh bind transform for '{sceneObject.Name}'.");
        var positions = geometry.Positions.Select(position => Vector3.Transform(position, meshBind)).ToArray();
        int skin = WriteSkin(actor, sceneObject, group, nodeMap, bindPose, bindings, inverseMeshBind);
        int meshNode = nodes.Count;
        nodes.Add(D("name", sceneObject.Name, "skin", skin,
            "mesh", Mesh(sceneObject, geometry, positions, bindings.Select(binding => binding.Source).ToArray(), meshBind),
            "extras", D("gaoKey", sceneObject.GaoKey.ToString("X8"), "actor", actor.Root.Name, "sourceId", sceneObject.Id)));
        // Joints include placement; applying the actor group to this mesh would transform it twice.
        roots.Add(meshNode);
        CaptureMorphTimeline(actor, sceneObject, meshNode);
    }

    private void AddChild(int parent, int child)
    {
        if (!nodes[parent].TryGetValue("children", out var children))
            nodes[parent]["children"] = children = new List<int>();
        ((List<int>)children).Add(child);
    }
}
