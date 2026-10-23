namespace JadeView;

internal static class GameScriptWorldListResolver
{
    internal static GameScriptWorldListResolution Resolve(GameScriptLiteralCommand command,
        GameWorldListDescriptor? descriptor)
    {
        if (command.Selector != GameWorldUniverseReducer.LoadListSelector)
            return new(GameScriptWorldListResolutionStatus.NotWorldListCommand, null, null);
        if (command.Arguments.Count != 1)
            return new(GameScriptWorldListResolutionStatus.InvalidArguments, null, null);

        uint requestedKey = command.Arguments[0];
        if (requestedKey is 0 or uint.MaxValue)
            return new(GameScriptWorldListResolutionStatus.InvalidKey, requestedKey, null);
        if (descriptor?.OuterListKey != requestedKey)
            return new(GameScriptWorldListResolutionStatus.DescriptorUnavailable, requestedKey, null);

        // This associates serialized inputs only. Loading/merging, callback dispatch,
        // resource decode success and runtime universe slots are not established here.
        return new(GameScriptWorldListResolutionStatus.MetadataMatched, requestedKey, descriptor);
    }
}
