namespace JadeView;

internal static class CinematicCompanionActionRequestResolver
{
    private const int CompanionStatusMessage = 49;
    private const int DeferredActionStatus = 4;
    private const string CompanionMessageCallback = "companion_co_exec_get_message";
    private const int ActionSetSelector = 0x3391;

    internal static CinematicCompanionActionRequestResolution Resolve(
        IEnumerable<(float Time, CinematicLiteralCall Call)> eventCalls,
        SceneActor actor,
        uint preferredWorldKey,
        IEnumerable<CinematicScriptOwner> scriptOwners)
    {
        CinematicScriptOwner[] scopedOwners = scriptOwners.ToArray();
        var actorKeys = actor.Members.Select(member => member.GaoKey).Append(actor.Root.GaoKey).ToHashSet();
        var requests = eventCalls
            .Where(item => actorKeys.Contains(item.Call.Target)
                && IsPreviewableCompanionActionRequest(item.Call))
            .Where(item => HasCompanionMessageHandler(item.Call.Target, preferredWorldKey, scopedOwners))
            .ToArray();
        var previewCalls = requests
            .Select(item => (item.Time, new CinematicLiteralCall(item.Call.Target, ActionSetSelector,
                [item.Call.Arguments[12]])))
            .ToArray();
        bool hasConditionalStatus = requests.Any(item => unchecked((int)item.Call.Arguments[11]) != 3);
        string statusGate = hasConditionalStatus
            ? " Non-status-3 messages also depend on old_companion_status or a changed nonzero world input, which are unavailable here."
            : string.Empty;
        string[] diagnostics = requests.Length == 0 ? [] :
        [
            $"{actor.Root.Name}: {requests.Length} companion MSG 49 action request(s) are previewed at their message times. "
                + "Iris defers this callback while co_current_mode is 9 or 17, but that runtime mode is not reconstructed from the BF snapshot."
                + statusGate
                + "In Iris, ACT_SetAction requested during EVE is queued for the following ANI pass: ANI advances the old action to the event's intra-frame time, switches actions, then consumes the remaining frame time. "
                + "JadeView maps the request to its source timestamp on a continuous timeline; the original engine frame cadence and exact rendered sub-frame boundary are unknown."
        ];
        return new(previewCalls, diagnostics);
    }

    private static bool HasCompanionMessageHandler(uint targetKey, uint preferredWorldKey,
        CinematicScriptOwner[] owners)
    {
        var owner = CinematicScriptOwnerResolver.Resolve(targetKey, preferredWorldKey, owners);
        return owner != null && CinematicScriptReader.TryDescribeCallbackReachability(owner,
            CompanionMessageCallback, out _);
    }

    private static bool IsPreviewableCompanionActionRequest(CinematicLiteralCall call)
    {
        if (CinematicEngineFunctionCatalog.NameFor(call.Selector) != "MSGSendAll"
            || call.Arguments.Length != 13)
            return false;

        int messageType = unchecked((int)call.Arguments[0]);
        int status = unchecked((int)call.Arguments[11]);
        int action = unchecked((int)call.Arguments[12]);
        return messageType == CompanionStatusMessage
            && status != DeferredActionStatus
            && action >= 0;
    }
}
