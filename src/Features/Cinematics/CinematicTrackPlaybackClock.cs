namespace JadeView;

internal readonly record struct CinematicTrackPlaybackCommand(float Time, string Operation, float? Value = null);
internal readonly record struct CinematicTrackPlaybackSnapshot(float LocalTime, bool Running,
    bool Paused, int RunIndex);
internal readonly record struct CinematicTrackPlaybackTransition(float Time, string Operation,
    CinematicTrackPlaybackSnapshot Before, CinematicTrackPlaybackSnapshot After);

internal sealed class CinematicTrackPlaybackClock
{
    internal const float MaximumUnwrappedSetTimeSeconds = ushort.MaxValue / 60f;
    private readonly Segment[] segments;
    private readonly float[] changeTimes;
    private readonly CinematicTrackPlaybackTransition[] transitions;

    internal IReadOnlyList<float> ChangeTimes => changeTimes;
    internal IReadOnlyList<CinematicTrackPlaybackTransition> Transitions => transitions;

    internal CinematicTrackPlaybackClock(float startTime, float trackDuration, float timelineDuration,
        int trackFlags, IEnumerable<(float Time, string Operation)> commands)
        : this(startTime, trackDuration, timelineDuration, trackFlags,
            commands.Select(command => new CinematicTrackPlaybackCommand(command.Time, command.Operation)),
            trackDuration, true)
    {
    }

    internal static CinematicTrackPlaybackClock FromCommands(float startTime, float trackDuration,
        float timelineDuration, int trackFlags, IEnumerable<CinematicTrackPlaybackCommand> commands,
        float finalEventStart) => new(startTime, trackDuration, timelineDuration, trackFlags,
            commands, finalEventStart, true);

    private CinematicTrackPlaybackClock(float startTime, float trackDuration, float timelineDuration,
        int trackFlags, IEnumerable<CinematicTrackPlaybackCommand> commands, float finalEventStart, bool _)
    {
        if (!float.IsFinite(startTime) || !float.IsFinite(trackDuration) || trackDuration < 0
            || !float.IsFinite(timelineDuration) || timelineDuration < startTime)
            throw new ArgumentOutOfRangeException(nameof(trackDuration), "Invalid cinematic event-track clock range.");

        var orderedCommands = commands
            .Where(command => float.IsFinite(command.Time) && command.Time >= startTime
                && command.Time <= timelineDuration)
            .OrderBy(command => command.Time)
            .ToArray();
        changeTimes = orderedCommands.Select(command => command.Time).Distinct().ToArray();
        var mappedSegments = new List<Segment>(orderedCommands.Length + 2);
        bool autoLoop = (trackFlags & CinematicEventTrackFlags.AutoLoop) != 0;
        float timelineResolution = MathF.BitIncrement(timelineDuration) - timelineDuration;
        bool loopPeriodBelowTimelineResolution = autoLoop && trackDuration > 0
            && trackDuration <= timelineResolution;
        bool autoStop = (trackFlags & CinematicEventTrackFlags.AutoStop) != 0;
        bool runningInit = (trackFlags & CinematicEventTrackFlags.RunningInit) != 0;
        float safeFinalEventStart = float.IsFinite(finalEventStart)
            ? Math.Clamp(finalEventStart, 0, trackDuration)
            : trackDuration;
        float cursor = startTime;
        float localTime = 0;
        bool running = true;
        bool paused = false;
        int runIndex = 0;
        var recordedTransitions = new List<CinematicTrackPlaybackTransition>
        {
            new(startTime, "Start", new(localTime, false, false, runIndex),
                new(localTime, running, paused, runIndex))
        };

        foreach (var command in orderedCommands)
        {
            AppendInterval(command.Time);
            var beforeCommand = new CinematicTrackPlaybackSnapshot(localTime, running, paused, runIndex);
            switch (command.Operation)
            {
                case "TRAPlay":
                case "TRAPlayAll":
                    if (!running)
                    {
                        localTime = 0;
                        running = true;
                        paused = false;
                        runIndex++;
                    }
                    break;
                case "TRAStop":
                case "TRAStopAll":
                    running = false;
                    break;
                case "TRAPause":
                case "TRAPauseAll":
                    paused = true;
                    break;
                case "TRAUnPause":
                case "TRAUnPauseAll":
                    paused = false;
                    break;
                case "TRASetEnd":
                    localTime = trackDuration;
                    break;
                case "TRASetTime" when command.Value is float requestedTime
                    && float.IsFinite(requestedTime) && requestedTime >= 0
                    && requestedTime <= MaximumUnwrappedSetTimeSeconds:
                    localTime = requestedTime >= trackDuration
                        ? safeFinalEventStart
                        : requestedTime;
                    break;
                case "TRAReinit":
                case "TRAReinitAll":
                    localTime = 0;
                    running = runningInit;
                    paused = false;
                    runIndex++;
                    break;
                case "EVEOwnerLoopReset" when running:
                    localTime = 0;
                    runIndex++;
                    break;
            }

            if (IsStateCommand(command.Operation))
                recordedTransitions.Add(new(command.Time, command.Operation,
                    beforeCommand, new(localTime, running, paused, runIndex)));
        }

        AppendInterval(timelineDuration);
        if (mappedSegments.Count == 0)
            mappedSegments.Add(new(startTime, startTime, 0, false, runIndex, running, paused));
        segments = mappedSegments.ToArray();
        transitions = recordedTransitions.OrderBy(transition => transition.Time).ToArray();

        void AppendInterval(float endTime)
        {
            endTime = Math.Clamp(endTime, cursor, timelineDuration);
            while (endTime > cursor)
            {
                bool hasTimeToAdvance = trackDuration > 0 && !loopPeriodBelowTimelineResolution
                    && (autoLoop || localTime < trackDuration);
                bool advances = running && !paused && hasTimeToAdvance;
                if (!advances)
                {
                    mappedSegments.Add(new(cursor, endTime, localTime, false, runIndex, running, paused));
                    cursor = endTime;
                    continue;
                }

                float remainingTrackTime = trackDuration - localTime;
                float activeEndTime = Math.Min(endTime, cursor + remainingTrackTime);
                if (activeEndTime <= cursor)
                {
                    float currentTimeResolution = MathF.BitIncrement(cursor) - cursor;
                    bool roundedLoopBoundary = localTime > 0 && remainingTrackTime <= currentTimeResolution;
                    if (!roundedLoopBoundary)
                    {
                        // The authored period is below the timeline's representable
                        // resolution. Hold the current pose instead of spinning on
                        // zero-length loop segments.
                        mappedSegments.Add(new(cursor, endTime, localTime, false, runIndex, running, paused));
                        cursor = endTime;
                        continue;
                    }

                    // At later cinematic times float precision can make the last
                    // fraction of a loop too small to advance the timeline cursor.
                    // Consume that boundary without emitting a zero-length segment.
                    var stateAtBoundary = new CinematicTrackPlaybackSnapshot(
                        localTime, running, paused, runIndex);
                    localTime = autoLoop ? 0 : trackDuration;
                    if (autoLoop)
                    {
                        runIndex++;
                        recordedTransitions.Add(new(cursor, "AutoLoop",
                            stateAtBoundary, new(localTime, running, paused, runIndex)));
                    }
                    else if (autoStop)
                    {
                        running = false;
                        paused = false;
                        recordedTransitions.Add(new(cursor, "AutoStop",
                            stateAtBoundary, new(localTime, running, paused, runIndex)));
                    }
                    continue;
                }
                mappedSegments.Add(new(cursor, activeEndTime, localTime, true, runIndex, running, paused));
                localTime += activeEndTime - cursor;
                cursor = activeEndTime;
                if (localTime < trackDuration) continue;

                if (autoLoop)
                {
                    var beforeLoop = new CinematicTrackPlaybackSnapshot(
                        localTime, running, paused, runIndex);
                    localTime = 0;
                    runIndex++;
                    recordedTransitions.Add(new(cursor, "AutoLoop",
                        beforeLoop, new(localTime, running, paused, runIndex)));
                }
                else
                {
                    localTime = trackDuration;
                    if (!autoStop) continue;
                    var beforeStop = new CinematicTrackPlaybackSnapshot(
                        localTime, running, paused, runIndex);
                    running = false;
                    paused = false;
                    recordedTransitions.Add(new(cursor, "AutoStop",
                        beforeStop, new(localTime, running, paused, runIndex)));
                }
            }
        }
    }

    internal bool TryGetSnapshot(float timelineTime, out CinematicTrackPlaybackSnapshot snapshot)
    {
        snapshot = default;
        if (!float.IsFinite(timelineTime) || segments.Length == 0 || timelineTime < segments[0].Start)
            return false;

        int low = 0;
        int high = segments.Length - 1;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (segments[middle].Start <= timelineTime) low = middle;
            else high = middle - 1;
        }

        Segment segment = segments[low];
        float clampedTime = Math.Min(timelineTime, segment.End);
        float localTime = segment.LocalStart + (segment.Advances ? clampedTime - segment.Start : 0);
        snapshot = new(localTime, segment.Running, segment.Paused, segment.RunIndex);
        var transitionAtTime = transitions.LastOrDefault(transition => transition.Time == timelineTime);
        if (transitionAtTime.Time == timelineTime)
            snapshot = transitionAtTime.After;
        return true;
    }

    private static bool IsStateCommand(string operation) => operation is
        "TRAPlay" or "TRAPlayAll" or "TRAStop" or "TRAStopAll"
        or "TRAPause" or "TRAPauseAll" or "TRAUnPause" or "TRAUnPauseAll"
        or "TRASetEnd" or "TRASetTime" or "TRAReinit" or "TRAReinitAll"
        or "EVEOwnerLoopReset";

    internal bool TryMap(float timelineTime, out float localTime)
    {
        localTime = 0;
        if (!float.IsFinite(timelineTime) || segments.Length == 0 || timelineTime < segments[0].Start)
            return false;

        int low = 0;
        int high = segments.Length - 1;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (segments[middle].Start <= timelineTime) low = middle;
            else high = middle - 1;
        }

        Segment segment = segments[low];
        float clampedTime = Math.Min(timelineTime, segment.End);
        localTime = segment.LocalStart + (segment.Advances ? clampedTime - segment.Start : 0);
        return true;
    }

    internal IEnumerable<float> TimelineTimesForLocal(float localTime)
    {
        if (!float.IsFinite(localTime)) yield break;
        var mappedRuns = new HashSet<int>();
        foreach (Segment segment in segments)
        {
            if (!segment.Advances) continue;
            float localEnd = segment.LocalStart + segment.End - segment.Start;
            if (localTime < segment.LocalStart || localTime > localEnd || !mappedRuns.Add(segment.RunIndex)) continue;
            yield return segment.Start + localTime - segment.LocalStart;
        }
    }

    internal IEnumerable<(float TimelineStart, float TimelineEnd, float LocalDuration)>
        TimelineIntervalsForLocalRange(float localStart, float localEnd)
    {
        if (!float.IsFinite(localStart) || !float.IsFinite(localEnd) || localEnd < localStart)
            yield break;
        if (localEnd == localStart)
        {
            foreach (float timelineTime in TimelineTimesForLocal(localStart))
                yield return (timelineTime, timelineTime, 0);
            yield break;
        }

        foreach (Segment segment in segments)
        {
            if (!segment.Advances) continue;
            float segmentLocalEnd = segment.LocalStart + segment.End - segment.Start;
            float overlapStart = Math.Max(localStart, segment.LocalStart);
            float overlapEnd = Math.Min(localEnd, segmentLocalEnd);
            if (overlapEnd <= overlapStart) continue;

            float timelineStart = segment.Start + overlapStart - segment.LocalStart;
            float timelineEnd = segment.Start + overlapEnd - segment.LocalStart;
            yield return (timelineStart, timelineEnd, overlapEnd - overlapStart);
        }
    }

    private readonly record struct Segment(float Start, float End, float LocalStart, bool Advances,
        int RunIndex, bool Running, bool Paused);
}
