namespace JadeView;

internal static class PcAppearanceCompanions
{
    // Temporary verified source pairing: Double H HD face and separate hair.
    // Replace with recovered complete shape data, not name-based guessing.
    internal static IReadOnlyList<AppearanceCompanionBinding> Bindings { get; } =
        Array.AsReadOnly(new[] { new AppearanceCompanionBinding(0x8F0066B2, 0xA900532C, 0xA90052F4) });
}
