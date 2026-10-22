namespace JadeView;

internal static class CinematicActorActionLinkResolver
{
    internal static int[] FindBaseEventActionSlots(CinematicScriptOwner owner,
        IReadOnlyDictionary<int, CinematicAction> actions)
    {
        if (owner.EventsKey is <= 2 or uint.MaxValue)
            return [];

        return actions
            .Where(action => action.Value.Items.Any(item => item.Clip.Key == owner.EventsKey))
            .Select(action => action.Key)
            .Order()
            .ToArray();
    }
}
