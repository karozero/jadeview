namespace JadeView;

internal sealed record GameScriptLiteralCommand(int Selector, IReadOnlyList<uint> Arguments);
