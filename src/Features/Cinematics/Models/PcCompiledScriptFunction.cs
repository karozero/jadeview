namespace JadeView;

/// <summary>
/// A bounded parse of the compact PC OFC payload candidate. The declared node
/// size is metadata only; serialized nodes and executable behavior are not
/// represented by this model.
/// </summary>
internal sealed record PcCompiledScriptFunction(
    int StackOffsetCount,
    int DeclaredNodeByteSize,
    byte[] LocalConstantBytes)
{
    /// <summary>
    /// Identifies this as a structurally validated compact-layout candidate,
    /// not proof of a native registry hit or runtime execution.
    /// </summary>
    internal const string LayoutStatus = "CompactCompiledLayoutCandidate";

    internal string InterpretationStatus => LayoutStatus;

}
