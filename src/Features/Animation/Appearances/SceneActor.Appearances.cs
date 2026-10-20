namespace JadeView;

public sealed partial class SceneActor
{
    private readonly Dictionary<int, int> appearanceOverrides = [];
    private readonly HashSet<int> resolvedMorphAppearances = [];

    public bool HasMorphAppearance(SceneObject member) =>
        member.Modifiers.Any(modifier => modifier.Type == 4) ||
        SourceAppearances(member).Any(model => model.Modifiers.Any(modifier => modifier.Type == 4));

    public bool ResolveMorphAppearance(SceneObject member, Func<SceneObject, int> countPoses)
    {
        int bone = Bones.IndexOf(member);
        if (bone < 0 || !resolvedMorphAppearances.Add(bone)) return false;
        // Keep a working native appearance. Only replace an empty/unreadable one.
        if (countPoses(member) > 0) return false;
        var models = SourceAppearances(member);
        int bestIndex = -1;
        int bestCount = 0;
        for (int index = 0; index < models.Count; index++)
        {
            int poseCount = countPoses(models[index]);
            if (poseCount <= bestCount) continue;
            bestIndex = index;
            bestCount = poseCount;
        }
        if (bestIndex < 0) return false;
        PreviewAppearance(member, bestIndex);
        return true;
    }

    public IReadOnlyList<SceneObject> SourceAppearances(SceneObject member)
    {
        int bone = Bones.IndexOf(member);
        return bone >= 0 && SourceMeshes.TryGetValue(bone, out var models) ? models : [];
    }

    public void PreviewAppearance(SceneObject member, int sourceIndex)
    {
        int bone = Bones.IndexOf(member);
        var models = SourceAppearances(member);
        if (bone < 0 || sourceIndex < 0 || sourceIndex >= models.Count)
            throw new ArgumentOutOfRangeException(nameof(sourceIndex));
        member.MorphPreview?.StopBlink();
        resolvedMorphAppearances.Add(bone);
        appearanceOverrides[bone] = sourceIndex;
        RestoreNativeAppearances();
        ApplyAppearanceOverrides();
        Evaluate(0);
    }

    public void RestoreClipAppearance(SceneObject member)
    {
        resolvedMorphAppearances.Add(Bones.IndexOf(member));
        member.MorphPreview?.StopBlink();
        appearanceOverrides.Remove(Bones.IndexOf(member));
        RestoreNativeAppearances();
        ApplyAppearanceOverrides();
        Evaluate(0);
    }

    private void ApplyAppearanceOverrides()
    {
        foreach (var (bone, source) in appearanceOverrides)
            if (Bones[bone] is { PreviewHidden: false } member)
                ApplySourceModel(member, SourceMeshes[bone][source]);
        ApplyAppearanceCompanions();
    }

    private void RestoreNativeAppearances()
    {
        ApplyShape(DefaultShape);
        if (Clip != null && ClipShapes.TryGetValue(Clip, out uint shape) && shape != DefaultShape) ApplyShape(shape);
    }

    private static void ApplySourceModel(SceneObject target, SceneObject model)
    {
        const uint skinPaletteFlags = 0x1200000;
        target.PreviewHidden = false;
        target.AppearanceRevision++;
        target.GeometryKey = model.GeometryKey;
        target.MaterialKey = model.MaterialKey;
        target.RliColors = model.RliColors;
        target.DrawFlags = model.DrawFlags;
        target.Modifiers = model.Modifiers;
        target.Flags = (target.Flags & ~skinPaletteFlags) | (model.Flags & skinPaletteFlags);
        if (target.ActorNode == null || model.ActorNode == null) return;
        target.ActorNode.Links = model.ActorNode.Links;
        target.ActorNode.Matrices = model.ActorNode.Matrices;
    }
}
