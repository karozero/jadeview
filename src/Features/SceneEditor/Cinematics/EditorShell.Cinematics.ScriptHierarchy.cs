using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private static void DrawCinematicHierarchyBindingDiagnostic(CinematicPlayback playback, WorldData world)
    {
        if (!ImGui.CollapsingHeader("Literal hierarchy bindings at playhead")) return;

        ImGui.Indent();
        ImGui.TextWrapped("Literal Set/Reset calls are evaluated from sampled event-time poses when endpoints resolve uniquely. Preview preserves world pose at attachment, follows the parent, and keeps continuity at detach. AI-selected parents, runtime raycasts, and engine-side collision/animation effects remain unavailable.");

        CinematicScriptHierarchyBinding[] bindings = playback.ScriptHierarchyBindingsAtPlayhead(world);
        if (bindings.Length == 0)
        {
            ImGui.TextDisabled("No decoded hierarchy Set/Reset calls affect this playhead.");
            ImGui.Unindent();
            return;
        }

        foreach (var group in bindings.GroupBy(binding => binding.Status).OrderBy(group => group.Key))
        {
            string groupLabel = $"{HierarchyBindingStatusName(group.Key)} ({group.Count()})";
            if (!ImGui.TreeNode(groupLabel)) continue;

            foreach (CinematicScriptHierarchyBinding binding in group.OrderBy(item => item.ChildKey))
                DrawHierarchyBindingRow(binding, world);

            ImGui.TreePop();
        }

        ImGui.Unindent();
    }

    private static void DrawHierarchyBindingRow(CinematicScriptHierarchyBinding binding, WorldData world)
    {
        string childLabel = ResolveHierarchyEndpointName(world, binding.ChildKey, binding.ChildEndpointCount);
        string parentLabel = binding.ParentKey is uint parentKey
            ? ResolveHierarchyEndpointName(world, parentKey, binding.ParentEndpointCount)
            : "Detached";
        string changeLabel = binding.ChangedAt is float time ? $"last change {time:F2}s" : "serialized parent";

        ImGui.BulletText($"Child {childLabel} · {EndpointCountLabel(binding.ChildEndpointCount)}");
        ImGui.Indent();
        string parentResolution = binding.ParentKey is null
            ? ""
            : $" · {EndpointCountLabel(binding.ParentEndpointCount)}";
        ImGui.TextUnformatted($"Parent {parentLabel}{parentResolution}");
        ImGui.TextDisabled($"{changeLabel} · pose {binding.PoseStatus}");
        ImGui.Unindent();
    }

    private static string ResolveHierarchyEndpointName(WorldData world, uint key, int endpointCount)
    {
        if (endpointCount != 1) return $"{key:X8}";
        SceneObject? sceneObject = world.Objects.FirstOrDefault(item => item.GaoKey == key);
        return sceneObject is null ? $"{key:X8}" : $"{sceneObject.Name} ({key:X8})";
    }

    private static string EndpointCountLabel(int endpointCount) => endpointCount switch
    {
        0 => "missing",
        1 => "unique",
        _ => $"ambiguous: {endpointCount} matches"
    };

    private static string HierarchyBindingStatusName(CinematicScriptHierarchyBindingStatus status) => status switch
    {
        CinematicScriptHierarchyBindingStatus.Detached => "Detached",
        CinematicScriptHierarchyBindingStatus.Bound => "Bound relation",
        CinematicScriptHierarchyBindingStatus.ChildMissing => "Child missing",
        CinematicScriptHierarchyBindingStatus.ChildAmbiguous => "Child ambiguous",
        CinematicScriptHierarchyBindingStatus.ParentMissing => "Parent missing",
        CinematicScriptHierarchyBindingStatus.ParentAmbiguous => "Parent ambiguous",
        CinematicScriptHierarchyBindingStatus.Cycle => "Cycle",
        _ => status.ToString()
    };
}
