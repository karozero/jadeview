namespace JadeView;

internal sealed record GameScriptWorldListResource(
    uint Key,
    string EntryName,
    GameWorldListDescriptor? Descriptor,
    string Status,
    string? Diagnostic);
