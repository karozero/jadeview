namespace JadeView;

internal sealed record ArchiveStartupScriptInventory(
    uint StartKey,
    string Status,
    string? InstanceName,
    uint? ModelKey,
    string? ModelName,
    uint? InstanceVariablesKey,
    string? InstanceVariablesName,
    ArchiveScriptResourceReference[] ModelResources,
    string? Diagnostic);

internal sealed record ArchiveScriptResourceReference(
    uint Key,
    string TypeTag,
    string Status,
    string? Name);
