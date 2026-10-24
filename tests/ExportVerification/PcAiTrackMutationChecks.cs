using JadeView;

internal static class PcAiTrackMutationChecks
{
    internal static void Verify()
    {
        var linkedFunctions = new Dictionary<uint, string>
        {
            [0x1234] = "next_track.ofc",
            [0xDEADBEEF] = "out_of_range_target.ofc"
        };
        var trackReplacement = Function(
            Node(0, 2, 0x21, 0x01, "Type"),
            Node(1, 0x1234, 0x27, 0x01, "Type"),
            Node(2, 0, 0x1785, 0x03, "Function", "AITrackChange"));
        PcAiTrackMutation replacement = PcAiTrackMutationAnalyzer.Analyze(trackReplacement, linkedFunctions).Single();
        Require(replacement == new PcAiTrackMutation(2, "AITrackChange", 2, 0x1234,
                "next_track.ofc", "TrackReplacementCandidate"),
            "A literal AITrackChange did not bind its track and function operands.");

        var trackStop = Function(
            Node(0, 4, 0x21, 0x01, "Type"),
            Node(1, 0, 0x1786, 0x03, "Function", "AITrackStop"));
        PcAiTrackMutation stop = PcAiTrackMutationAnalyzer.Analyze(trackStop, linkedFunctions).Single();
        Require(stop == new PcAiTrackMutation(1, "AITrackStop", 4, null, null, "TrackStopCandidate"),
            "A literal AITrackStop did not resolve the selected track.");

        var currentTrack = Function(
            Node(0, 0x1234, 0x27, 0x01, "Type"),
            Node(1, 0, 0x1787, 0x03, "Function", "AITrackCurChange"),
            Node(2, 0x1234, 0x27, 0x01, "Type"),
            Node(3, 0, 0x1789, 0x03, "Function", "AITrackCurChangeNow"),
            Node(4, 0, 0x1788, 0x03, "Function", "AITrackCurStop"));
        PcAiTrackMutation[] currentMutations = PcAiTrackMutationAnalyzer.Analyze(currentTrack, linkedFunctions);
        Require(currentMutations[0] == new PcAiTrackMutation(1, "AITrackCurChange", null,
                    0x1234, "next_track.ofc", "CurrentTrackChangeCandidate")
                && currentMutations[1] == new PcAiTrackMutation(3, "AITrackCurChangeNow", null,
                    0x1234, "next_track.ofc", "CurrentTrackChangeCandidate")
                && currentMutations[2] == new PcAiTrackMutation(4, "AITrackCurStop", null,
                    null, null, "CurrentTrackStop"),
            "Current-track change and stop operations were conflated with numbered-track changes.");

        var missingFunction = Function(
            Node(0, 0xBAD, 0x27, 0x01, "Type"),
            Node(1, 0, 0x1787, 0x03, "Function", "AITrackCurChange"));
        PcAiTrackMutation missingTarget = PcAiTrackMutationAnalyzer.Analyze(missingFunction, linkedFunctions).Single();
        Require(missingTarget.TargetKey == 0xBAD && missingTarget.TargetName == null
                && missingTarget.Status == "PointerRefNotLinkedByModel",
            "An unlinked AI track target was assigned a name or dropped from the evidence.");

        var unresolved = Function(
            Node(0, 7, 0x21, 0x01, "Type"),
            Node(1, unchecked((int)0xDEADBEEF), 0x27, 0x01, "Type"),
            Node(2, 0, 0x1785, 0x03, "Function", "AITrackChange"),
            Node(3, 0x9999, 0x21, 0x14, "Global variable"),
            Node(4, 0, 0x1786, 0x03, "Function", "AITrackStop"));
        PcAiTrackMutation[] unresolvedMutations = PcAiTrackMutationAnalyzer.Analyze(unresolved, linkedFunctions);
        Require(unresolvedMutations[0].TrackIndex == 7
                && unresolvedMutations[0].Status == "TrackIndexOutOfRange"
                && unresolvedMutations[0].TargetKey == 0xDEADBEEF
                && unresolvedMutations[1].TrackIndex == null
                && unresolvedMutations[1].Status == "DynamicTrackIndex",
            "Out-of-range or dynamic AI track operands were guessed as valid runtime state.");

        var indirectTarget = Function(
            Node(0, 1, 0x21, 0x01, "Type"),
            Node(1, 8, 0x21, 0x01, "Type"),
            Node(2, 0, 0x1785, 0x03, "Function", "AITrackChange"));
        PcAiTrackMutation indirect = PcAiTrackMutationAnalyzer.Analyze(indirectTarget, linkedFunctions).Single();
        Require(indirect.TrackIndex == 1 && indirect.TargetKey == null
                && indirect.Status == "IndirectOrNonLiteralTarget",
            "A non-pointerref AITrackChange target was treated as a literal function key.");

        Console.WriteLine("PASS: literal AI track replacements, current-track transitions, stops, and unresolved operands remain source-scoped and non-executing");
    }

    private static PcSerializedAiFunction Function(params PcAiInstruction[] nodes) =>
        new(0, nodes, [], [], [], []);

    private static PcAiInstruction Node(int index, int value, ushort value2, byte category,
        string categoryName, string? symbol = null) =>
        new(index, value, value2, 0, category, categoryName, symbol, null, null);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
