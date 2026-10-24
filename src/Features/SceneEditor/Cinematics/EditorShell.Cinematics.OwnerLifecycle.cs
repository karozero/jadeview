using ImGuiNET;

namespace JadeView;

public sealed partial class EditorShell
{
    private void DrawCinematicOwnerStartLifecycle(CinematicScriptOwner owner)
    {
        if (!ImGui.CollapsingHeader("Owner start lifecycle")) return;
        IReadOnlyList<CinematicScriptEventTrack> tracks = Document?.CinematicScriptEventTracks
            .GetValueOrDefault(owner.Id) ?? [];
        CinematicOwnerStartLifecycle lifecycle = CinematicOwnerStartLifecycleAnalyzer.Analyze(owner, tracks);

        ImGui.TextDisabled("Serialized links and source-derived scheduling stages. Runtime barriers remain unresolved; callbacks are never run here.");
        for (int phaseIndex = 0; phaseIndex < lifecycle.Phases.Length; phaseIndex++)
        {
            CinematicOwnerLifecyclePhase phase = lifecycle.Phases[phaseIndex];
            string evidenceLabel = phase.Evidence switch
            {
                CinematicOwnerLifecycleEvidence.ArchiveResolved => "archive",
                CinematicOwnerLifecycleEvidence.PartiallyResolved => "partial",
                _ => "runtime unknown"
            };
            string phaseId = $"##OwnerLifecycle{owner.WorldKey:X8}{owner.OwnerKey:X8}{phaseIndex}";
            if (!ImGui.TreeNode($"{phase.Title} · {evidenceLabel}{phaseId}")) continue;
            ImGui.TextWrapped(phase.Summary);
            foreach (string detail in phase.Details)
                ImGui.BulletText(detail);
            ImGui.TreePop();
        }
    }
}
