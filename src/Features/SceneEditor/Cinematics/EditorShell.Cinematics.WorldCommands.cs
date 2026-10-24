namespace JadeView;

public sealed partial class EditorShell
{
    private Archive? _scriptWorldListCacheArchive;
    private readonly Dictionary<uint, GameScriptWorldListResource> _scriptWorldListResources = [];

    private string DescribeCinematicWorldListBinding(CinematicLiteralCall call)
    {
        uint? requestedKey = call.Arguments.Length == 1 ? call.Arguments[0] : null;
        GameScriptWorldListResource? resource = requestedKey is { } key && key is not (0 or uint.MaxValue)
            ? GetScriptWorldListResource(key)
            : null;
        GameWorldListDescriptor? descriptor = resource?.Descriptor;
        var resolution = GameScriptWorldListResolver.Resolve(
            new GameScriptLiteralCommand(call.Selector, call.Arguments), descriptor);
        return resolution.Status switch
        {
            GameScriptWorldListResolutionStatus.MetadataMatched =>
                $"WOL {resolution.RequestedKey:X8} · {resource!.EntryName}: serialized merge roots [{string.Join(", ", resolution.Descriptor!.OrderedWorldRootKeys.Select(key => key.ToString("X8")))}]. The command resolves this archive resource; runtime loading, merging and universe slot are not evaluated.",
            GameScriptWorldListResolutionStatus.DescriptorUnavailable =>
                resource?.Diagnostic is { Length: > 0 } diagnostic
                    ? $"WOL {resolution.RequestedKey:X8}: {diagnostic}"
                    : $"WOL {resolution.RequestedKey:X8}: no matching WOL resource metadata is available.",
            GameScriptWorldListResolutionStatus.InvalidArguments =>
                "WORLoadList requires exactly one literal key; this command cannot be resolved.",
            GameScriptWorldListResolutionStatus.InvalidKey =>
                "WORLoadList has a null/reserved key; no loadable container is associated.",
            _ => ""
        };
    }

    private GameScriptWorldListResource? GetScriptWorldListResource(uint key)
    {
        if (Document?.WorldList is { OuterListKey: { } documentKey } descriptor
            && documentKey == key)
            return new(key, Document.ContainerName, descriptor, "Resolved", null);
        if (Archive == null) return null;

        if (!ReferenceEquals(_scriptWorldListCacheArchive, Archive))
        {
            _scriptWorldListCacheArchive = Archive;
            _scriptWorldListResources.Clear();
        }
        if (_scriptWorldListResources.TryGetValue(key, out GameScriptWorldListResource? cached))
            return cached;

        GameScriptWorldListResource resource = GameScriptWorldListArchiveReader.Read(Archive, key);
        _scriptWorldListResources[key] = resource;
        return resource;
    }
}
