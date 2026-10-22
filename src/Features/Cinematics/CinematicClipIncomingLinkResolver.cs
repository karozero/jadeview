namespace JadeView;

internal sealed record CinematicClipIncomingLink(float Time, uint TargetKey, string Description);

internal static class CinematicClipIncomingLinkResolver
{
    internal static CinematicClipIncomingLink[] Find(uint targetKey,
        IEnumerable<(float Time, CinematicLiteralCall Call)> calls,
        IEnumerable<(float Time, CinematicTimelineMessage Message)> messages)
    {
        if (targetKey is 0 or uint.MaxValue) return [];

        var links = calls
            .Where(item => item.Call.Target == targetKey)
            .Select(item => new CinematicClipIncomingLink(item.Time, item.Call.Target,
                $"{CinematicEngineFunctionCatalog.NameFor(item.Call.Selector)} target"))
            .Concat(messages.SelectMany(item => MessageReferences(item.Time, item.Message, targetKey)))
            .DistinctBy(link => (link.Time, link.TargetKey, link.Description))
            .OrderBy(link => link.Time)
            .ThenBy(link => link.Description, StringComparer.Ordinal)
            .ToArray();
        return links;
    }

    private static IEnumerable<CinematicClipIncomingLink> MessageReferences(float time,
        CinematicTimelineMessage message, uint targetKey)
    {
        if (message.ExecutionObjectKey == targetKey)
            yield return new(time, targetKey, $"type {message.Type} EVE execution object");
        if (message.Gao == targetKey)
            yield return new(time, targetKey, $"type {message.Type} message GAO");
        if (message.Gao2 == targetKey)
            yield return new(time, targetKey, $"type {message.Type} message GAO2");
    }
}
