namespace JadeView;

public sealed partial class FaceMorphPreview
{
    private readonly PcMorphProfile? sourceProfile;
    public IReadOnlyList<int> BlinkGroups => sourceProfile?.BlinkGroups ?? Array.Empty<int>();
    public string BlinkTitle => sourceProfile?.BlinkTitle ?? "PC head blink";
    public bool CanBakeBlink => sourceProfile?.CanBakeBlink == true;
    public bool SupportsPcBlink => BlinkGroups.Count > 0;
    public MorphExpressionPresetSet? ExpressionPresets => sourceProfile?.ExpressionPresets;

    // Compatibility query; UI and preview behavior use profile capabilities.
    public bool SupportsSallyBlink => sourceProfile?.Id == PcMorphProfiles.SallyId;
}
