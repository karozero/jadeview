namespace JadeView;

internal sealed record GameScriptWorldListResolution(GameScriptWorldListResolutionStatus Status,
    uint? RequestedKey, GameWorldListDescriptor? Descriptor);

internal enum GameScriptWorldListResolutionStatus
{
    NotWorldListCommand,
    InvalidArguments,
    InvalidKey,
    DescriptorUnavailable,
    MetadataMatched
}
