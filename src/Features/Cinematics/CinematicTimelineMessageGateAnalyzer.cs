namespace JadeView;

internal static class CinematicTimelineMessageGateAnalyzer
{
    internal static float? FindFirstMatchingMessageTime(CinematicScriptOwner owner,
        uint worldKey, uint eventKey, float duration,
        IEnumerable<CinematicScriptMessageTrack> eventTracks,
        IEnumerable<CinematicScriptNamedReference> namedReferences,
        IEnumerable<CinematicScheduledMessage>? scheduledMessages = null)
    {
        if (!float.IsFinite(duration) || duration < 0) return null;

        var trackArray = eventTracks.ToArray();
        var candidateTracks = trackArray
            .Where(track => track.WorldKey == worldKey && track.EventKey == eventKey)
            .Where(track => track.LinkName.Equals("Cinematic", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var references = namedReferences.ToArray();
        var decodedMessages = CinematicScriptBehaviorAnalyzer.FindMessageGateCandidates(
            owner, candidateTracks, references) ?? [];
        var engineMessages = CinematicScriptBehaviorAnalyzer.FindExtendedMessageGateCandidates(
            owner, candidateTracks, references) ?? [];

        var candidateTimes = decodedMessages.Select(candidate => candidate.Time)
            .Concat(engineMessages.Select(candidate => candidate.Time))
            .Concat(FindScheduledMessageTimes(owner, trackArray, references, scheduledMessages))
            .Where(time => float.IsFinite(time) && time >= 0 && time <= duration)
            .Select(time => (float?)time);
        return candidateTimes.Min();
    }

    private static IEnumerable<float> FindScheduledMessageTimes(CinematicScriptOwner owner,
        IEnumerable<CinematicScriptMessageTrack> eventTracks,
        IEnumerable<CinematicScriptNamedReference> namedReferences,
        IEnumerable<CinematicScheduledMessage>? scheduledMessages)
    {
        if (scheduledMessages == null) return [];

        var sceneEventTracks = eventTracks
            .Where(track => track.LinkName.Equals("SceneEvents", StringComparison.OrdinalIgnoreCase)
                && track.ChannelIndex is not null)
            .ToArray();
        if (sceneEventTracks.Length == 0) return [];

        var candidates = CinematicScriptBehaviorAnalyzer.FindMessageGateCandidates(
            owner, sceneEventTracks, namedReferences) ?? [];
        var schedules = scheduledMessages
            .Where(message => message.QueueOwnerEvidence == CinematicMessageQueueOwnerEvidence.IrisEveTrackContext
                && message.IrisQueueOwnerCandidateKey is not null)
            .ToArray();
        if (candidates.Length == 0 || schedules.Length == 0) return [];

        return candidates.SelectMany(candidate => schedules
            .Where(schedule => IsScheduledCandidate(candidate, schedule))
            .Select(schedule => schedule.TimelineTime));
    }

    private static bool IsScheduledCandidate(CinematicScriptMessageGateCandidate candidate,
        CinematicScheduledMessage schedule)
    {
        if (candidate.Track.ChannelIndex is not { } channelIndex
            || schedule.WorldKey != candidate.Track.WorldKey
            || schedule.WorldKey != candidate.QueueObject.WorldKey
            || schedule.EventListOwnerKey != candidate.Track.OwnerKey
            || schedule.EventKey != candidate.Track.EventKey
            || schedule.ChannelIndex != channelIndex
            || schedule.ChannelSourceTime != candidate.Time
            || schedule.IrisQueueOwnerCandidateKey != candidate.QueueObject.Key)
            return false;

        return schedule.Message with { Time = candidate.Time } == candidate.Message;
    }
}
