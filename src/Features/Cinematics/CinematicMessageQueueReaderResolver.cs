namespace JadeView;

internal sealed class CinematicMessageQueueReaderResolver
{
    private readonly IReadOnlyDictionary<CinematicScriptOwnerId, CinematicScriptOwner[]> ownersById;
    private readonly Dictionary<(CinematicScriptOwnerId OwnerId, int MessageType), string> descriptions = [];

    internal CinematicMessageQueueReaderResolver(IEnumerable<CinematicScriptOwner> owners)
    {
        ownersById = owners.GroupBy(owner => owner.Id)
            .ToDictionary(group => group.Key, group => group.ToArray());
    }

    internal string DescribePotentialReaders(CinematicScheduledMessage scheduled)
    {
        if (scheduled.QueueOwnerEvidence != CinematicMessageQueueOwnerEvidence.IrisEveTrackContext
            || scheduled.IrisQueueOwnerCandidateKey is not { } queueOwnerKey)
            return "Iris queue candidate unresolved; no reader attribution";

        var receiverId = new CinematicScriptOwnerId(scheduled.WorldKey, queueOwnerKey);
        return descriptions.GetValueOrDefault((receiverId, scheduled.Message.Type))
            ?? DescribeAndCache(receiverId, scheduled.Message.Type);
    }

    private string DescribeAndCache(CinematicScriptOwnerId receiverId, int messageType)
    {
        CinematicScriptOwner[] receiverCandidates = ownersById.GetValueOrDefault(receiverId, []);
        if (receiverCandidates.Length != 1)
            return Cache(receiverId, messageType, receiverCandidates.Length == 0
                ? $"Iris queue candidate {receiverId.OwnerKey:X8}; no script owner indexed in this world"
                : $"Iris queue candidate {receiverId.OwnerKey:X8}; script owner is ambiguous in this world");

        CinematicScriptOwner receiver = receiverCandidates[0];
        CinematicScriptMessageReader[] readers = CinematicScriptReader
            .ReachableMessageReaders(receiver, messageType);
        if (readers.Length == 0)
            return Cache(receiverId, messageType,
                $"Iris queue owner {receiver.Name} ({receiverId.OwnerKey:X8}); no source-confirmed reader for message {messageType} in reachable callbacks");

        string[] readerDescriptions = readers.Take(3).Select(reader =>
            $"{reader.FunctionName} ({reader.ReadApi}) via {reader.CallbackPath}").ToArray();
        string additionalReaders = readers.Length > readerDescriptions.Length
            ? $" (+{readers.Length - readerDescriptions.Length} more)" : "";
        return Cache(receiverId, messageType, $"possible queue reader on {receiver.Name} ({receiverId.OwnerKey:X8}): "
            + string.Join("; ", readerDescriptions) + additionalReaders
            + "; callback dispatch and delivery are unverified");
    }

    private string Cache(CinematicScriptOwnerId receiverId, int messageType, string description)
    {
        descriptions[(receiverId, messageType)] = description;
        return description;
    }
}
