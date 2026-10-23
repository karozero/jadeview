namespace JadeView;

public sealed partial class EditorShell
{
    private string DescribeCinematicWorldListBinding(CinematicLiteralCall call)
    {
        var resolution = GameScriptWorldListResolver.Resolve(
            new GameScriptLiteralCommand(call.Selector, call.Arguments), Document?.WorldList);
        return resolution.Status switch
        {
            GameScriptWorldListResolutionStatus.MetadataMatched =>
                $"WOL {resolution.RequestedKey:X8}: serialized merge roots [{string.Join(", ", resolution.Descriptor!.OrderedWorldRootKeys.Select(key => key.ToString("X8")))}]. Metadata match only; execution and universe slot are unknown.",
            GameScriptWorldListResolutionStatus.DescriptorUnavailable =>
                $"WOL {resolution.RequestedKey:X8}: matching container metadata is not loaded; roots and runtime state are unknown.",
            GameScriptWorldListResolutionStatus.InvalidArguments =>
                "WORLoadList requires exactly one literal key; this command cannot be resolved.",
            GameScriptWorldListResolutionStatus.InvalidKey =>
                "WORLoadList has a null/reserved key; no loadable container is associated.",
            _ => ""
        };
    }
}
