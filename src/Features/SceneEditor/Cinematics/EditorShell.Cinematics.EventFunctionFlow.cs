using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private static void DrawCinematicEventFunctionFlow(CinematicEmbeddedEventTrackList eventList,
        CinematicEmbeddedEventTrack track)
    {
        if (track.SourceTrack is not { } sourceTrack) return;

        CinematicEventFunctionControlFlow[] functionEvents =
            CinematicEventFunctionControlFlowReader.Read(eventList, track);
        if (functionEvents.Length == 0) return;

        EditorUi.DrawCategoryTitle($"EVE AI-function flow ({functionEvents.Length} keys)");
        ImGui.TextDisabled("Iris runs these AI nodes at EVE key boundaries. A wait flag can keep the cursor on that key and re-evaluate the node on a later EVE update.");
        foreach (CinematicEventFunctionControlFlow functionEvent in functionEvents.Take(24))
        {
            string behavior = functionEvent.TestedGlobalFlag is { } flag
                ? DescribeGlobalFlagWait(functionEvent.WaitMode, flag)
                : functionEvent.WaitMode switch
            {
                CinematicEventFunctionWaitMode.WaitWhileTrue =>
                    "holds and retries while the node returns true; advances when false",
                CinematicEventFunctionWaitMode.WaitWhileFalse =>
                    "holds and retries while the node returns false; advances when true",
                _ => "no serialized return-value wait flag; node behavior is not evaluated"
            };
            ImGui.BulletText($"Key {functionEvent.KeyIndex} · {functionEvent.Time:F3}s · {behavior} · flags 0x{functionEvent.KeyFlags:X4}");
            if (functionEvent.WaitMode != CinematicEventFunctionWaitMode.None
                && functionEvent.DownstreamKeyCount > 0)
            {
                string affectedRange = functionEvent.FirstDownstreamKeyTime is { } firstTime
                    && functionEvent.LastDownstreamKeyTime is { } lastTime
                    ? $"{firstTime:F3}–{lastTime:F3}s"
                    : "unknown time range";
                ImGui.TextDisabled($"  May delay {functionEvent.DownstreamKeyCount} later keys on this channel ({affectedRange}); preview keeps authored times.");
            }
        }
        if (functionEvents.Length > 24)
            ImGui.TextDisabled($"{functionEvents.Length - 24} additional AI-function keys omitted.");
        bool containsBlockingWait = functionEvents.Any(functionEvent =>
            functionEvent.WaitMode != CinematicEventFunctionWaitMode.None);
        if (containsBlockingWait)
            ImGui.TextDisabled("Later keys on this channel can drift from their serialized times: Iris retries a wait key and resumes only after its condition changes. The preview keeps the serialized timing.");
        ImGui.TextDisabled("This static preview does not execute the AI node or its side effects. Iris suppresses AI-function keys during world-load animation-only setup; JadeView does not observe live game execution.");
    }

    private static string DescribeGlobalFlagWait(CinematicEventFunctionWaitMode waitMode, uint flag) =>
        waitMode switch
        {
            CinematicEventFunctionWaitMode.WaitWhileTrue =>
                $"tests global save flag {flag}; holds while set, advances when cleared",
            CinematicEventFunctionWaitMode.WaitWhileFalse =>
                $"tests global save flag {flag}; holds while cleared, advances when set",
            _ => $"tests global save flag {flag}; no serialized return-value wait flag"
        };
}
