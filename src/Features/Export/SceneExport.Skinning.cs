using System.Numerics;

namespace JadeView;

public sealed partial class SceneExporter
{
    private List<ExportSkinBinding> SkinBindings(SceneActor actor, SceneObject sceneObject,
        Geometry geometry, Dictionary<SceneObject, int> nodeMap)
    {
        var bindings = new List<ExportSkinBinding>();
        if ((sceneObject.Flags & 0x200000) == 0 || sceneObject.ActorNode == null)
            return bindings;
        foreach (var source in geometry.Skin)
        {
            int? parent = SkinParent(actor, sceneObject, source.Bone, nodeMap);
            if (!parent.HasValue)
            {
                warnings.Add($"'{sceneObject.Name}' has an unresolved skin binding {source.Bone}; affected weights are omitted.");
                continue;
            }
            int joint = nodes.Count;
            nodes.Add(D("name", $"{sceneObject.Name} bind {bindings.Count}"));
            AddChild(parent.Value, joint);
            bindings.Add(new(source, joint));
        }
        return bindings;
    }

    private int? SkinParent(SceneActor actor, SceneObject sceneObject, int index,
        Dictionary<SceneObject, int> nodeMap)
    {
        var palette = sceneObject.ActorNode!;
        if (index < 0) return null;
        if ((sceneObject.Flags & 0x1000000) != 0)
        {
            if (index >= palette.Links.Length) return null;
            var link = palette.Links[index];
            var target = link.Index == -1 ? actor.Root : link.Index >= 0 && link.Index < actor.Bones.Count
                ? actor.Bones[link.Index] : actor.Members.FirstOrDefault(member => member.GaoKey == link.Key);
            return target != null && nodeMap.TryGetValue(target, out int node) ? node : null;
        }
        if (index >= palette.Matrices.Length) return null;
        int parent = nodes.Count;
        nodes.Add(D("name", $"{sceneObject.Name} palette {index}",
            "matrix", Matrix(AnimationMath.ToRender(palette.Matrices[index]))));
        AddChild(nodeMap[actor.Root], parent);
        return parent;
    }

    private int WriteSkin(SceneActor actor, SceneObject sceneObject, int group,
        Dictionary<SceneObject, int> nodeMap, Dictionary<SceneObject, Matrix4x4> bindPose,
        List<ExportSkinBinding> bindings, Matrix4x4 inverseMeshBind)
    {
        int fallback = nodes.Count;
        nodes.Add(D("name", sceneObject.Name + " unweighted"));
        AddChild(nodeMap[sceneObject], fallback);
        // Put every inverse bind in the same actor/world rest space as the ancestors.
        // A single mesh-space change preserves each influence exactly: P*M*inverse(M)*IBM.
        // Mixing geometry-local binds with world-space ancestors stretches Blender's rest bones.
        var inverse = new List<float>();
        foreach (var binding in bindings)
            inverse.AddRange(Matrix(inverseMeshBind * AnimationMath.ToRender(AnimationMath.SkinInverseBind(binding.Source, sceneObject.DrawFlags))));
        inverse.AddRange(Matrix(inverseMeshBind));
        var joints = bindings.Select(binding => binding.Joint).Append(fallback).ToList();
        foreach (var ancestor in actor.Members)
        {
            if (!Matrix4x4.Invert(AnimationMath.ToRender(bindPose[ancestor]), out var ancestorBind))
                throw new InvalidDataException($"Singular bind transform for '{ancestor.Name}'.");
            joints.Add(nodeMap[ancestor]);
            inverse.AddRange(Matrix(ancestorBind));
        }
        // The placement container is the armature object, not a deforming joint.
        if (rootDisplayTips.TryGetValue(actor, out var tip))
        {
            joints.Add(tip.Node);
            inverse.AddRange(Matrix(tip.InverseBind));
        }
        int skinIndex = skins.Count;
        skins.Add(D("joints", joints.ToArray(), "inverseBindMatrices", Floats(inverse.ToArray(), 16, "MAT4"), "skeleton", group));
        return skinIndex;
    }
}
