namespace JadeView;

internal static class PcMorphSourceIssues
{
    private const string EyelidMessage = "Known eyelid artifacts when this raw target is applied alone. PC controller/baseline behavior is unresolved; source deltas are preserved.";

    // Revision required: controller/baseline semantics must be qualified before
    // correcting these deltas. Only exact observed payloads get this warning.
    internal static IReadOnlyList<MorphSourceIssueProfile> Entries { get; } = Array.AsReadOnly(new[]
    {
        new MorphSourceIssueProfile(0x620030FB, 37, "548EE3B453CF9A36C41FDD587C2F8A0A600DB551501FD44B0BE9522FBBCED6B1", EyelidMessage),
        new MorphSourceIssueProfile(0x6200318D, 37, "BFAEC59F0F5DC7630DE2D120A8D95D5FC495A8B9C0077770144BA2717CEA3EAC", EyelidMessage)
    });
}
