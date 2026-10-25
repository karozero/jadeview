namespace JadeView;

internal sealed record CinematicScriptFunctionResource(
    uint FunctionKey,
    uint WorldKey,
    string Name,
    int PayloadSize,
    string Status,
    PcCompiledScriptFunction? Metadata,
    string? Diagnostic)
{
    internal PcSerializedAiFunction? SerializedFunction { get; init; }
}
