using System.Numerics;

namespace JadeView;

/// <summary>
/// Converts decoded calls from a scheduled SceneEvents channel into shared game-script
/// command semantics. This adapter does not run the command or evaluate non-EVE callbacks.
/// </summary>
internal static class CinematicGameScriptEventAdapter
{
    private const string MoveFunction = "OBJMove";
    private const int VectorArgumentCount = 3;

    internal static GameScriptMoveCommand[] ReadMoveCommands(
        CinematicEmbeddedEventTrackList eventList, CinematicEmbeddedEventTrack channel)
    {
        return channel.Calls
            .Where(item => CinematicEngineFunctionCatalog.NameFor(item.Call.Selector) == MoveFunction
                && item.Call.Arguments.Length == VectorArgumentCount)
            .Select(item => Create(eventList, channel, item.Time, item.Call))
            .OfType<GameScriptMoveCommand>()
            .ToArray();
    }

    internal static GameScriptMoveSegment[] ReadMoveSegments(
        CinematicEmbeddedEventTrackList eventList, CinematicEmbeddedEventTrack channel,
        float channelStartTime, float timelineDuration, CinematicTrackPlaybackClock? playbackClock,
        ICollection<string>? diagnostics = null)
    {
        if (!float.IsFinite(channelStartTime) || !float.IsFinite(timelineDuration)
            || timelineDuration < channelStartTime)
            return [];

        var segments = new List<GameScriptMoveSegment>();
        foreach (var command in ReadMoveCommands(eventList, channel))
        {
            if (command.EventDurationSeconds is not { } duration || !float.IsFinite(duration) || duration <= 0)
            {
                diagnostics?.Add($"OBJMove at {command.Origin.SourceTimeSeconds:G6}s on owner "
                    + $"{command.Origin.OwnerKey:X8}, channel {command.Origin.ChannelIndex}: "
                    + "the matching source key duration is missing, invalid, or ambiguous; movement was withheld.");
                continue;
            }

            float localStart = command.Origin.SourceTimeSeconds;
            float localEnd = localStart + duration;
            if (!float.IsFinite(localEnd)) continue;

            if (playbackClock == null)
            {
                float timelineStart = channelStartTime + localStart;
                float timelineEnd = Math.Min(timelineDuration, timelineStart + duration);
                AddSegment(timelineStart, timelineEnd, timelineEnd - timelineStart);
                continue;
            }

            foreach (var interval in playbackClock.TimelineIntervalsForLocalRange(localStart, localEnd))
                AddSegment(interval.TimelineStart, interval.TimelineEnd, interval.LocalDuration);

            void AddSegment(float timelineStart, float timelineEnd, float elapsedEventSeconds)
            {
                if (!float.IsFinite(timelineStart) || !float.IsFinite(timelineEnd)
                    || !float.IsFinite(elapsedEventSeconds) || timelineEnd <= timelineStart
                    || elapsedEventSeconds <= 0)
                    return;
                segments.Add(new(command, timelineStart, timelineEnd, elapsedEventSeconds));
            }
        }

        return segments.OrderBy(segment => segment.TimelineStartSeconds)
            .ThenBy(segment => segment.Command.Origin.SourceTimeSeconds)
            .ToArray();
    }

    private static GameScriptMoveCommand? Create(CinematicEmbeddedEventTrackList eventList,
        CinematicEmbeddedEventTrack channel, float sourceTime, CinematicLiteralCall call)
    {
        if (!float.IsFinite(sourceTime) || call.Target is 0 or uint.MaxValue) return null;
        Vector3 displacement = ReadVector(call.Arguments);
        if (!IsFinite(displacement)) return null;

        float? duration = FindUnambiguousDuration(channel, sourceTime);
        return new(call.Target, displacement, GameScriptExecutionContext.EveEvent, duration,
            new(eventList.WorldKey, eventList.OwnerKey, eventList.EventKey,
                channel.Index, sourceTime));
    }

    private static float? FindUnambiguousDuration(CinematicEmbeddedEventTrack channel, float sourceTime)
    {
        float[] matchingDurations = channel.SourceTrack?.Keys
            .Where(key => key.Time == sourceTime)
            .Select(key => key.Duration)
            .Distinct()
            .ToArray() ?? [];
        if (matchingDurations.Length != 1) return null;
        float duration = matchingDurations[0];
        return float.IsFinite(duration) && duration > 0 ? duration : null;
    }

    private static Vector3 ReadVector(uint[] arguments) => new(
        BitConverter.UInt32BitsToSingle(arguments[0]),
        BitConverter.UInt32BitsToSingle(arguments[1]),
        BitConverter.UInt32BitsToSingle(arguments[2]));

    private static bool IsFinite(Vector3 value) => float.IsFinite(value.X)
        && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
