namespace JadeView;

internal sealed record CinematicCompanionActionRequestResolution(
    (float Time, CinematicLiteralCall Call)[] PreviewCalls,
    string[] Diagnostics);
