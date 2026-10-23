namespace JadeView;

internal static class CinematicScriptMessageFlowResolver
{
    internal static CinematicScriptMessageRoute[] Resolve(CinematicScriptOwner sender,
        IEnumerable<CinematicScriptOwner> owners)
    {
        var ownerArray = owners.DistinctBy(owner => owner.Id).ToArray();
        var sourceReferences = CinematicScriptReader.ReachableSourceReferences(sender);
        var sendCalls = CinematicScriptReader.ReachableEngineCalls(sender)
            .Where(call => call.Api == "MSGSend" && call.Arguments.Length >= 2)
            .ToArray();
        var routes = new List<CinematicScriptMessageRoute>();

        foreach (var call in sendCalls)
        {
            if (!TryReadMessageId(call, out int messageId)
                || !TryResolveRecipientKey(sender, call, sourceReferences, out uint recipientKey))
                continue;

            var receiver = ResolveUniqueOwner(recipientKey, sender.WorldKey, ownerArray);
            if (receiver == null) continue;
            var readers = CinematicScriptReader.ReachableMessageReaders(receiver, messageId);
            if (readers.Length == 0) continue;

            string? targetName = receiver.Name;
            routes.Add(new(messageId, recipientKey, targetName, call, sender.Name, receiver, readers));
        }

        return routes.DistinctBy(route => (route.MessageId, route.RecipientKey,
                route.SendCall.FunctionName, route.SendCall.Source, route.SendCall.Line,
                route.Receiver.Id))
            .OrderBy(route => route.MessageId)
            .ThenBy(route => route.Receiver.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(route => route.SendCall.FunctionName, StringComparer.Ordinal)
            .ThenBy(route => route.SendCall.Line).ToArray();
    }

    private static bool TryReadMessageId(CinematicScriptReachableEngineCall call, out int messageId)
    {
        messageId = 0;
        var writes = call.MessageFieldWrites?.Where(candidate => candidate.Name == "msgID").ToArray() ?? [];
        if (writes.Length != 1 || writes[0].BranchConditions.Length != 0) return false;
        return int.TryParse(writes[0].Expression, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out messageId);
    }

    private static bool TryResolveRecipientKey(CinematicScriptOwner sender,
        CinematicScriptReachableEngineCall call,
        CinematicScriptReachableSourceReference[] sourceReferences, out uint recipientKey)
    {
        var keys = new HashSet<uint>();
        string targetExpression = call.Arguments[0].Trim();
        if (targetExpression == "AI_gpst_CurrentGameObject")
            keys.Add(sender.OwnerKey);

        foreach (var reference in CinematicScriptArgumentBindingResolver
                     .ResolveSourceObjectReferences(call, sourceReferences))
            keys.Add(reference.Key);

        foreach (var field in CinematicScriptArgumentBindingResolver.ResolveOwnerFields(call, sender))
        {
            if (field.ObjectKey is { } objectKey && CinematicObjectKey.IsArchiveObject(objectKey))
                keys.Add(objectKey);
        }

        if (keys.Count == 1)
        {
            recipientKey = keys.Single();
            return true;
        }

        recipientKey = 0;
        return false;
    }

    private static CinematicScriptOwner? ResolveUniqueOwner(uint ownerKey, uint preferredWorldKey,
        IEnumerable<CinematicScriptOwner> owners)
    {
        var candidates = owners.Where(owner => owner.OwnerKey == ownerKey).ToArray();
        var preferredCandidates = candidates.Where(owner => owner.WorldKey == preferredWorldKey).ToArray();
        if (preferredCandidates.Length == 1) return preferredCandidates[0];
        return preferredCandidates.Length == 0 && candidates.Length == 1 ? candidates[0] : null;
    }
}

internal sealed record CinematicScriptMessageRoute(int MessageId, uint RecipientKey,
    string? RecipientName, CinematicScriptReachableEngineCall SendCall, string SenderName,
    CinematicScriptOwner Receiver, CinematicScriptMessageReader[] Readers);
