using JadeView;

internal static class CinematicAnimationModeTimelineChecks
{
    internal static void Verify()
    {
        var calls = new (float Time, CinematicLiteralCall Call)[]
        {
            (1, new(100, 0x32d3, [0, 3])),
            (2, new(100, 0x32d3, [1, 2])),
            (3, new(100, 0x32d3, [4, 1])),
            (4, new(100, 0x32d3, [0])),
            (2, new(200, 0x32d3, [0, 3])),
            (8, new(100, 0x32d3, [0, 7]))
        };

        CinematicAnimationModeEvent[] events = CinematicAnimationModeTimeline.Read(
            calls, 100, [1, 2], [2.5f, 5], 6);

        CinematicAnimationModeEvent[] modeRequests = events
            .Where(item => item.Kind is CinematicAnimationModeEventKind.LiteralModeSetRequest
                or CinematicAnimationModeEventKind.MalformedModeSetCall)
            .ToArray();
        Check(modeRequests.Length == 4,
            "Mode timeline did not filter by target and playhead or retain malformed calls.");
        Check(modeRequests[0].ModeValue == 3
            && modeRequests[0].Meaning == CinematicAnimationModeMeaning.SpeedFromDynamics
            && modeRequests[0].TrackWithinEngineCapacity,
            "Mode 3 or track 0 was not decoded as a bounded source request.");
        Check(modeRequests[1].ModeValue == 2
            && modeRequests[1].Meaning == CinematicAnimationModeMeaning.EncodedButUnnamed,
            "Encoded mode 2 was incorrectly assigned a source meaning.");
        Check(!modeRequests[2].TrackWithinEngineCapacity
            && modeRequests[2].Meaning == CinematicAnimationModeMeaning.UpdateFrequency,
            "Out-of-capacity track index was reported as a supported track.");
        Check(modeRequests[3].Kind == CinematicAnimationModeEventKind.MalformedModeSetCall,
            "Malformed ANI_ModeSet arguments were not retained as a diagnostic event.");

        CinematicAnimationModeEvent[] boundaries = events
            .Where(item => item.Kind is CinematicAnimationModeEventKind.BodyActionStart
                or CinematicAnimationModeEventKind.GameplayHandoff)
            .ToArray();
        Check(boundaries.Length == 4
            && boundaries.Count(item => item.Kind == CinematicAnimationModeEventKind.BodyActionStart) == 2
            && boundaries.Count(item => item.Kind == CinematicAnimationModeEventKind.GameplayHandoff) == 2,
            "Action-start resets or gameplay handoff were not retained in the event ledger.");
        Check(modeRequests[0].SharesTimestamp,
            "Same-time mode/action events did not retain the unresolved ordering warning.");
        Check(!modeRequests[1].AfterGameplayHandoff,
            "A request before the first handoff was marked as post-handoff.");
        Check(modeRequests[2].AfterGameplayHandoff,
            "A mode request after an earlier handoff was hidden by a later handoff.");

        Console.WriteLine("PASS: ANI_ModeSet timeline preserves typed requests, action resets, handoffs, and unknown semantics");

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
    }
}
