namespace JadeView;

public sealed record MorphExpressionPresetSet(string Name, IReadOnlyList<int> Kits,
    Func<int, int, float[]> SettledWeights, string? Notes = null);
