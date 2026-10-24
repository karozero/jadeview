using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private static void DrawCinematicAnimationModeDiagnostic(CinematicPlayback playback)
    {
        if (!ImGui.CollapsingHeader("Animation mode requests")) return;

        ImGui.Indent();
        ImGui.TextWrapped("Decoded ANI_ModeSet requests are shown with reconstructed body-action starts and gameplay handoffs. This is an event ledger, not proof that a callback ran or a mode became effective. DYN movement is not simulated.");

        var actorsWithEvents = playback.BodyTimelines
            .Select(item => (item.Actor, Events: CinematicAnimationModeTimeline.Read(
                playback.ScriptStateCalls, item.Actor.Root.GaoKey,
                item.Body.ActionModeResetTimes, item.Body.GameplayHandoffTimes, playback.Time)))
            .Where(item => item.Events.Any(entry => entry.Kind is
                CinematicAnimationModeEventKind.LiteralModeSetRequest
                or CinematicAnimationModeEventKind.MalformedModeSetCall))
            .ToArray();

        if (actorsWithEvents.Length == 0)
        {
            ImGui.TextDisabled("No ANI_ModeSet calls for body-timeline actors have occurred by this playhead.");
            ImGui.Unindent();
            return;
        }

        foreach (var item in actorsWithEvents)
        {
            if (!ImGui.TreeNode($"{item.Actor.Root.Name} · {item.Actor.Root.GaoKey:X8} ({item.Events.Length})"))
                continue;

            foreach (CinematicAnimationModeEvent entry in item.Events.TakeLast(40))
                DrawAnimationModeEvent(entry);

            if (item.Events.Length > 40)
                ImGui.TextDisabled($"Showing the latest 40 of {item.Events.Length} events.");
            ImGui.TreePop();
        }

        ImGui.Unindent();
    }

    private static void DrawAnimationModeEvent(CinematicAnimationModeEvent entry)
    {
        string detail = entry.Kind switch
        {
            CinematicAnimationModeEventKind.LiteralModeSetRequest => ModeRequestLabel(entry),
            CinematicAnimationModeEventKind.MalformedModeSetCall => "ANI_ModeSet call has unsupported argument count",
            CinematicAnimationModeEventKind.BodyActionStart => "Reconstructed body action start; source reloads mode from the action item",
            CinematicAnimationModeEventKind.GameplayHandoff => "Gameplay handoff; subsequent runtime mode is unknown",
            _ => entry.Kind.ToString()
        };

        string ordering = entry.SharesTimestamp ? " · same-time ordering unverified" : "";
        string handoff = entry.AfterGameplayHandoff ? " · after gameplay handoff" : "";
        ImGui.BulletText($"{entry.Time:F2}s · {detail}{ordering}{handoff}");
    }

    private static string ModeRequestLabel(CinematicAnimationModeEvent entry)
    {
        string track = entry.TrackIndex is uint trackIndex
            ? $"track {trackIndex} {(entry.TrackWithinEngineCapacity ? "within" : "outside")} engine capacity"
            : "track unknown";
        string mode = entry.ModeValue switch
        {
            0 => "mode 0: speed from animation",
            1 => "mode 1: update frequency",
            2 => "mode 2: encoded, source meaning unnamed",
            3 => "mode 3: speed from dynamics",
            uint value => $"mode {value}: outside known 0–3 encoding",
            _ => "mode unknown"
        };
        return $"ANI_ModeSet request · {track} · {mode}";
    }
}
