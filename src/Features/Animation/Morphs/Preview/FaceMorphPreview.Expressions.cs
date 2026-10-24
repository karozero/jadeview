namespace JadeView;

public sealed partial class FaceMorphPreview
{
    public void PreviewExpression(int group)
    {
        if (!Expressions.Any(expression => expression.Group == group))
            throw new ArgumentOutOfRangeException(nameof(group), "No nonzero source morph for this pose.");
        Reset();
        SetWeightValue(group, 1);
    }

    public void PreviewSallyExpression(int kit)
    {
        if (!SupportsSallyBlink) throw new InvalidOperationException("PC Sally expression table is not qualified for this model.");
        PreviewExpressionKit(kit);
    }

    public void PreviewExpressionKit(int kit)
    {
        var presets = ExpressionPresets ?? throw new InvalidOperationException("No qualified expression presets for this model.");
        var settled = presets.SettledWeights(kit, weights.Length);
        Reset();
        ReplaceWeights(settled);
    }
}
