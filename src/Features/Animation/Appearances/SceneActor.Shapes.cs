namespace JadeView;

public sealed partial class SceneActor
{
    private uint? appliedShapeKey;

    public void ApplyShape(uint key)
    {
        appliedShapeKey = key;
        if (!Shapes.TryGetValue(key, out var shape)) return;
        UpdateModuleMap(shape);
        for (int entry = 0; entry < shape[0]; entry++)
        {
            int offset = 1 + entry * 3;
            if (offset + 2 >= shape.Length) break;
            int bone = (sbyte)shape[offset];
            int source = (sbyte)shape[offset + 2];
            ApplyShapeEntry(bone, source);
        }
    }

    private void ApplyShapeEntry(int bone, int source)
    {
        bool validBone = bone >= 0 && bone < Bones.Count;
        if (!validBone || Bones[bone] is not { } target) return;
        if (source == -3) { target.PreviewHidden = true; return; }
        if (source == -2) { target.PreviewHidden = false; return; }
        if (source < 0 || !SourceMeshes.TryGetValue(bone, out var models) || source >= models.Count) return;
        ApplySourceModel(target, models[source]);
    }
}
