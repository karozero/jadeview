namespace JadeView;

internal sealed record CinematicUnmappedCallback(CinematicScriptOwner Owner,
    CinematicScriptCallbackSlot Slot);

internal static class CinematicScriptCallbackCoverage
{
    internal static CinematicCallbackSlotRange? ResolveConfiguredRange(
        IEnumerable<CinematicScriptCallbackSlot> callbackSlots)
    {
        int[] configuredSlots = callbackSlots.Where(slot => slot.FunctionKey != 0)
            .Select(slot => slot.Index).ToArray();
        return configuredSlots.Length == 0
            ? null
            : new(configuredSlots.Min(), configuredSlots.Max());
    }

    internal static CinematicUnmappedCallback[] FindUnmapped(IEnumerable<CinematicScriptOwner> owners) =>
        owners.SelectMany(owner => (owner.CallbackSlots ?? [])
            .Where(slot => slot.FunctionKey != 0 && slot.FunctionName == null)
            .Select(slot => new CinematicUnmappedCallback(owner, slot)))
            .ToArray();
}

internal readonly record struct CinematicCallbackSlotRange(int FirstSlot, int LastSlot);
