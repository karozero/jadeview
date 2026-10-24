using System.Numerics;
using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawLightComponent(SceneObject sceneObject)
    {
        DrawCategoryTitle("Source component", "Serialized light data from the object's PC GRO resource. These values do not establish whether scripts enable the light at the current time.");
        ImGui.TextUnformatted($"Resource: {sceneObject.LightResourceKey:X8}");
        if (sceneObject.Light is not { } light)
        {
            ImGui.TextWrapped("The referenced light component could not be decoded. See scene diagnostics for the reason.");
            return;
        }

        ImGui.TextUnformatted($"Type code: {light.TypeCode} ({light.KnownType?.ToString() ?? "Unknown"})");
        ImGui.TextUnformatted($"Flags: {light.Flags:X8}");
        DrawCategoryTitle("Color and range");
        uint packed = light.Color;
        var color = new Vector4(packed & 255, packed >> 8 & 255, packed >> 16 & 255, packed >> 24) / 255f;
        ImGui.ColorButton("Serialized light color", color);
        ImGui.SameLine();
        ImGui.TextUnformatted($"Packed color: {packed:X8}");
        ImGui.TextUnformatted($"Near: {light.Near:G6}   Far: {light.Far:G6}");
        ImGui.TextUnformatted($"Density: {light.Density:G6}   I8: {light.I8:G6}");
        DrawCategoryTitle("Object reference");
        ImGui.TextUnformatted(light.HasLightGaoReference
            ? $"Referenced GAO: {light.LightGaoKey:X8}"
            : "No referenced GAO");
        DrawEvaluatedLight(sceneObject);
        DrawCategoryTitle("Preview coverage");
        ImGui.TextWrapped("World ambient, material/RLI colors and supported dynamic diffuse lights are evaluated. Specular, runtime sectors and animated bounds remain incomplete.");
    }

    private void DrawEvaluatedLight(SceneObject sceneObject)
    {
        if (_cinematicPlayback == null) return;
        var state = _cinematicPlayback.LightState;
        var evaluated = state.Lights.FirstOrDefault(item => ReferenceEquals(item.Receiver, sceneObject));
        if (evaluated == null) return;
        DrawCategoryTitle("Evaluated light", "Sampled world-cycle sun/fog fields and timed literal EVE setters. Literal setters take precedence where native callback order is unknown. Other AI callbacks can change the live game's state further.");
        var light = evaluated.Light;
        ImGui.TextUnformatted($"At {_cinematicPlayback.Time:F2}s: flags {light.Flags:X8}, color {light.Color:X8}");
        ImGui.TextUnformatted($"Near {light.Near:G6} · Far {light.Far:G6} · related GAO {light.LightGaoKey:X8}");
        foreach (var diagnostic in state.Diagnostics.Where(item => item.Call.Target == sceneObject.GaoKey))
            ImGui.TextWrapped($"{diagnostic.Time:F2}s · {diagnostic.Status}: {diagnostic.Detail}");
        var cycleDiagnostics = state.WorldCycleDiagnostics
            .Where(item => item.ReceiverKey == sceneObject.GaoKey).ToArray();
        if (cycleDiagnostics.Length == 0) return;
        DrawCategoryTitle("World-cycle limits");
        foreach (var diagnostic in cycleDiagnostics)
            ImGui.TextWrapped($"{diagnostic.Field} · {diagnostic.Code}: {diagnostic.Detail}");
    }
}
