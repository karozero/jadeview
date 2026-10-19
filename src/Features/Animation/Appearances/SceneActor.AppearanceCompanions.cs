namespace JadeView;

public sealed partial class SceneActor
{
    private void ApplyAppearanceCompanions()
    {
        var selectedGeometry = appearanceOverrides.Select(selection =>
            SourceMeshes[selection.Key][selection.Value].RenderGeometryKey).ToHashSet();
        foreach (var binding in PcAppearanceCompanions.Bindings)
        {
            if (!selectedGeometry.Contains(binding.TriggerGeometry)) continue;
            ApplyAppearanceCompanion(binding);
        }
    }

    private void ApplyAppearanceCompanion(AppearanceCompanionBinding binding)
    {
        var member = Members.FirstOrDefault(candidate => candidate.GaoKey == binding.CompanionBone);
        if (member == null || appearanceOverrides.ContainsKey(Bones.IndexOf(member))) return;
        var source = SourceAppearances(member).FirstOrDefault(model => model.RenderGeometryKey == binding.SourceGeometry);
        if (source != null) ApplySourceModel(member, source);
    }
}
