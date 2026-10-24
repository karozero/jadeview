namespace JadeView;

internal enum CinematicOwnerLifecycleEvidence
{
    ArchiveResolved,
    PartiallyResolved,
    RuntimeUnknown
}

internal sealed record CinematicOwnerLifecyclePhase(
    string Title,
    CinematicOwnerLifecycleEvidence Evidence,
    string Summary,
    string[] Details);

internal sealed record CinematicOwnerStartLifecycle(CinematicOwnerLifecyclePhase[] Phases);
