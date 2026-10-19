namespace JadeView;

internal static class PcMorphProfiles
{
    internal const string SallyId = "pc-sally";

    // Temporary verified profiles. Replacing these with recovered controller
    // bindings requires archive/runtime evidence, not similarity of group IDs.
    private static readonly PcMorphProfile[] Profiles =
    [
        new()
        {
            Id = SallyId, GeometryKeys = [0x620030FB, 0x6200318D], GroupCount = 55, ChannelCount = 6,
            ReferenceRows = new() { [0] = [1, 1], [1] = [2, 2] },
            BlinkGroups = Array.AsReadOnly(new[] { 1, 2 }), BlinkTitle = "PC Sally blink", CanBakeBlink = true,
            ExpressionPresets = new("PC Sally expressions", SallyExpressionPresets.Kits, SallyExpressionPresets.SettledWeights,
                "Kits 3 and 5 share the same pose.")
        },
        new()
        {
            Id = "pc-peyj-hd", GeometryKeys = [0x8E001060], GroupCount = 22, ChannelCount = 7,
            ReferenceRows = new() { [0] = [10] },
            BlinkGroups = Array.AsReadOnly(new[] { 10 }), RequireNonzeroBlink = true
        },
        new()
        {
            Id = "pc-military-hd", GeometryKeys = [0x8F0066B2], GroupCount = 16, ChannelCount = 5,
            ReferenceRows = new() { [0] = [1, 10, uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue] },
            EmptyGroups = [1], BlinkGroups = Array.AsReadOnly(new[] { 10 }), RequireNonzeroBlink = true
        }
    ];

    internal static PcMorphProfile? Match(uint geometry, FaceMorphData? data) => data == null
        ? null : Profiles.FirstOrDefault(profile => profile.Matches(geometry, data));
}
