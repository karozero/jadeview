namespace JadeView;

internal static class CinematicCompanionActionRequestResolver
{
    private const int CompanionStatusMessage = 49;
    private const int DeferredActionStatus = 4;
    private const string CompanionMessageCallback = "companion_co_exec_get_message";
    private const int ActionSetSelector = 0x3391;

    internal static (float Time, CinematicLiteralCall Call)[] Resolve(
        IEnumerable<(float Time, CinematicLiteralCall Call)> eventCalls,
        SceneActor actor,
        uint preferredWorldKey,
        IEnumerable<CinematicScriptOwner> scriptOwners)
    {
        CinematicScriptOwner[] scopedOwners = scriptOwners.ToArray();
        var actorKeys = actor.Members.Select(member => member.GaoKey).Append(actor.Root.GaoKey).ToHashSet();
        return eventCalls
            .Where(item => actorKeys.Contains(item.Call.Target)
                && IsImmediateCompanionActionRequest(item.Call))
            .Where(item => HasCompanionMessageHandler(item.Call.Target, preferredWorldKey, scopedOwners))
            .Select(item => (item.Time, new CinematicLiteralCall(item.Call.Target, ActionSetSelector,
                [item.Call.Arguments[12]])))
            .ToArray();
    }

    private static bool HasCompanionMessageHandler(uint targetKey, uint preferredWorldKey,
        CinematicScriptOwner[] owners)
    {
        var owner = CinematicScriptOwnerResolver.Resolve(targetKey, preferredWorldKey, owners);
        return owner != null && CinematicScriptReader.TryDescribeCallbackReachability(owner,
            CompanionMessageCallback, out _);
    }

    private static bool IsImmediateCompanionActionRequest(CinematicLiteralCall call)
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
