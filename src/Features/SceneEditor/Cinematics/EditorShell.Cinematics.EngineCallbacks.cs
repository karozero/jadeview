using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private static void DrawCinematicEngineCallbackAssignments(CinematicScriptOwner owner)
    {
        if (owner.EngineCallbackSlots == null)
        {
            ImGui.TextDisabled("Engine-event callback assignments were not decoded from the model OVA.");
            return;
        }

        CinematicScriptEngineCallbackSlot[] configuredCallbacks = owner.EngineCallbackSlots
            .Where(callback => callback.FunctionKey != 0)
            .ToArray();
        EditorUi.DrawCategoryTitle($"Engine-event callback assignments ({configuredCallbacks.Length})");
        if (configuredCallbacks.Length == 0)
        {
            ImGui.TextDisabled("No optional engine-event callback functions are assigned in this model OVA.");
            return;
        }

        ImGui.TextDisabled("These are hook assignments, separate from the five AI track callbacks. Assignment does not prove the engine hook fired.");
        foreach (var callback in configuredCallbacks)
        {
            string function = callback.FunctionName is { } name
                ? CinematicScriptReader.DescribeCallbackName(name)
                : $"unmapped function {callback.FunctionKey:X8}";
            ImGui.BulletText($"ID {callback.EventId:D2} · {CinematicEngineCallbackCatalog.Describe(callback.EventId)} → {function}");
            DrawEngineCallbackDispatchSites(callback.EventId);
        }
        ImGui.TextWrapped("Call sites identify where Iris requests a dispatch, but runtime instance gates and branches are not evaluated here.");
    }

    private static void DrawCinematicCallbackDispatchSite(CinematicScriptCallbackDispatchSite site)
    {
        ImGui.BulletText($"{site.ExecutionContext} · {site.Source}:{site.Line}");
        if (!ImGui.TreeNode($"Dispatch conditions##{site.CallbackId}_{site.Source}_{site.Line}"))
            return;

        ImGui.TextWrapped($"Iris source function: {site.EnclosingFunction} · {site.Call} · {site.ContextEvidence}");
        ImGui.TextWrapped($"Caller-side condition: {site.CallerEligibility}");
        ImGui.TextWrapped($"Dispatcher-side condition: {site.DispatcherEligibility}");
        ImGui.TextWrapped($"Callback target: {site.TargetResolution}");
        ImGui.TextDisabled("These source conditions are not evaluated against live world, draw, animation, or callback-registration state.");
        ImGui.TreePop();
    }
}
