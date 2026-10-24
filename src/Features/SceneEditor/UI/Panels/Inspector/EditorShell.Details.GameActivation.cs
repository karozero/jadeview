using System.Numerics;
using ImGuiNET;
using static JadeView.EditorUi;

namespace JadeView;

public sealed partial class EditorShell
{
    private static void DrawInitialScriptActivation(SceneObject sceneObject)
    {
        DrawCategoryTitle("Game activation", "Iris initial-world-load prediction from serialized GAO flags.");

        GameScriptInitialActivationForecast forecast = GameScriptInitialActivationForecaster
            .Evaluate(sceneObject.Flags, sceneObject.SerializedStateFlags);
        ImGui.TextDisabled($"GAO identity flags: 0x{sceneObject.Flags:X8} · serialized state: 0x{forecast.SerializedStateFlags:X8}");
        ImGui.TextDisabled($"Projected initial runtime flags2: 0x{forecast.InitialRuntimeFlags2:X8}");

        foreach (GameScriptActivationPathForecast path in forecast.Paths)
        {
            bool eligible = path.Eligibility == GameScriptActivationEligibility.Eligible;
            Vector4 statusColor = eligible ? Green : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled];
            ImGui.TextColored(statusColor,
                $"{path.WorldMode}: {(eligible ? "eligible" : "blocked")}");
            ImGui.Indent();
            ImGui.TextWrapped(path.Reason);
            if (path.EotMembership is { } membership)
            {
                string tables = membership.TableNames.Length == 0
                    ? "no recognized EOT update table"
                    : string.Join(", ", membership.TableNames);
                ImGui.TextDisabled($"If activated, scheduled for: {tables}.");
            }
            ImGui.Unindent();
        }

        ImGui.TextWrapped(forecast.Scope);
        ImGui.TextDisabled("Activation and EOT membership do not prove that an object is visible, rendered, or active in the retail game's current runtime.");
    }
}
