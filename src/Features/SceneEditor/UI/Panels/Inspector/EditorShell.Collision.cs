using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawCollisionResourceInspector(SceneObject sceneObject)
    {
        if (Document?.CollisionResources?.TryGet(sceneObject, out var resources) != true)
            return;

        DrawCategoryTitle("Source collision", "Serialized source data for this GAO. This is read-only and does not simulate collision in the viewport.");
        ImGui.TextUnformatted($"Source GAO {resources.SourceGaoKey:X8} · world {resources.SourceWorldKey:X8}");
        if (!string.IsNullOrWhiteSpace(resources.SourceDiagnostic))
            ImGui.TextWrapped($"GAO source resolution: {resources.SourceDiagnostic}");

        DrawCategoryTitle("Serialized references");
        ImGui.TextUnformatted($"Instance: {FormatCollisionKey(resources.InstanceKey)}");
        ImGui.TextUnformatted($"Map: {FormatCollisionKey(resources.MapKey)}");
        DrawCategoryTitle("Instance");
        DrawCollisionResource(resources.InstanceKey, resources.Instance);
        if (resources.Set != null)
        {
            DrawCategoryTitle("Referenced ColSet");
            DrawCollisionResource(resources.Set.Key, resources.Set);
        }
        DrawCategoryTitle("Map and COB references");
        DrawCollisionResource(resources.MapKey, resources.Map);
    }

    private static string FormatCollisionKey(uint key) =>
        key is 0 or uint.MaxValue ? "none" : $"{key:X8}";

    private static void DrawCollisionResource(uint key, PcCollisionResourceInfo? resource)
    {
        if (key is 0 or uint.MaxValue) return;
        if (resource == null)
        {
            ImGui.TextUnformatted("Not available in the loaded source scope.");
            return;
        }

        ImGui.TextUnformatted($"Status: {resource.Status} · kind '{resource.Kind}' · source world {resource.SourceWorldKey:X8}");
        if (!string.IsNullOrWhiteSpace(resource.Diagnostic))
            ImGui.TextWrapped(resource.Diagnostic);
        foreach (string warning in resource.Warnings)
            ImGui.TextWrapped(warning);

        if (resource.Instance is { } instance)
        {
            ImGui.TextUnformatted($"Set key {instance.ColSetKey:X8} · zones {instance.ZoneCount} (shared {instance.SharedZoneCount}, specific {instance.SpecificZoneCount})");
            if (instance.SpecificZones.Length > 0)
            {
                string specificShapes = string.Join(", ", instance.SpecificZones
                    .GroupBy(zone => zone.Shape.Kind)
                    .OrderBy(group => group.Key)
                    .Select(group => $"{group.Key}: {group.Count()}"));
                ImGui.TextWrapped($"Inline specific shapes: {specificShapes}");
            }
        }
        if (resource.Set is { } set)
        {
            var counts = set.Zones.GroupBy(zone => zone.Shape.Kind)
                .OrderBy(group => group.Key)
                .Select(group => $"{group.Key}: {group.Count()}");
            ImGui.TextWrapped($"Zones: {set.Zones.Length} · {string.Join(", ", counts)}");
        }
        if (resource.Map is { } map)
        {
            ImGui.TextUnformatted($"COB keys: {map.CobKeys.Length}");
            foreach (uint cobKey in map.CobKeys)
                ImGui.BulletText($"{cobKey:X8}");
        }
    }
}
