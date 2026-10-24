namespace JadeView;

internal static class PcMorphControllerSequences
{
    // Source initializer 7100AF12 / 009763A0; update 7100B928 / 005ADBF0.
    // See docs/face-morph-pc-controller-variants.md and docs/data/pc-controller-7100AF12.json.
    internal static readonly PcMorphControllerBinding FactorAware = new(0x7100AF12, 0x7100B928,
        Array.AsReadOnly(new[]
        {
            50, 31, 50, 31, 31, 54, 31, 50, 51, 31, 52, 31, 50, 51, 31, 52,
            50, 50, 31, 31, 51, 52, 31, 52, 31, 50, 51, 54, 31, 31, 52, 31
        }), PcMorphTransitionCompletion.ProgressionAndFactor);

    // Initializer 35001FF5 / A07680; update 9E002C81 / 4F7D30.
    private const uint SharedInitializerKey = 0x35001FF5;
    private const uint SharedUpdateKey = 0x9E002C81;
    internal const uint CinematicMouthInitializerKey = 0x3500B774;
    internal const uint CinematicMouthUpdateKey = 0x3500B77E;
    internal const uint CinematicMouthMessageKey = 0x3500B773;
    private static readonly IReadOnlyDictionary<int, PcMorphCinematicMouthVariant> CinematicMouthVariants =
        new Dictionary<int, PcMorphCinematicMouthVariant>
        {
            [1] = Variant(4, null,
                7, 6, 7, 6, 6, 8, 6, 7, 8, 6, 9, 6, 7, 8, 6, 9,
                7, 7, 6, 6, 8, 9, 6, 7, 6, 8, 6, 9, 6, 7, 8, 6),
            [2] = Variant(3, null,
                7, 6, 7, 6, 6, 8, 6, 7, 8, 6, 9, 6, 7, 8, 6, 9,
                7, 7, 6, 6, 8, 9, 6, 7, 6, 9, 6, 6, 8, 7, 8, 6),
            [3] = Variant(2, null,
                5, 0, 5, 0, 0, 3, 3, 0, 5, 3, 0, 4, 0, 5, 3, 3,
                0, 4, 5, 5, 0, 0, 3, 3, 4, 0, 5, 0, 3, 3, 4, 0),
            [4] = Variant(1, 3,
                1, 0, 1, 0, 0, 3, 0, 1, 3, 0, 2, 0, 1, 3, 0, 2,
                1, 1, 0, 0, 3, 2, 0, 1, 0, 3, 2, 0, 0, 1, 3, 0),
            [5] = Variant(4, 4,
                7, 6, 7, 6, 6, 8, 6, 7, 8, 6, 9, 6, 7, 8, 6, 9,
                7, 7, 6, 6, 8, 9, 6, 7, 6, 9, 6, 6, 8, 8, 7, 6),
            [6] = Variant(4, null,
                9, 0, 9, 0, 0, 10, 0, 9, 10, 0, 11, 0, 9, 10, 0, 11,
                9, 9, 0, 0, 10, 11, 0, 11, 0, 9, 0, 0, 10, 10, 11, 0),
            [7] = Variant(4, null,
                5, 0, 5, 0, 0, 4, 0, 5, 4, 0, 6, 0, 5, 4, 0, 6,
                5, 5, 0, 0, 4, 6, 0, 6, 0, 5, 0, 0, 4, 4, 6, 0)
        };
    private static readonly PcMorphControllerBinding FriendHead = new(SharedInitializerKey, SharedUpdateKey,
        Array.AsReadOnly(new[]
        {
            6, 4, 4, 4, 1, 4, 5, 5, 5, 3, 5, 1, 1, 4, 4, 3,
            4, 4, 1, 5, 5, 4, 1, 4, 5, 5, 1, 6, 4, 3, 4, 1
        }),
        PcMorphTransitionCompletion.ProgressionThreshold);
    private static readonly PcMorphControllerBinding OtherHead = new(SharedInitializerKey, SharedUpdateKey,
        Array.AsReadOnly(new[]
        {
            4, 1, 4, 1, 1, 3, 1, 4, 3, 1, 5, 1, 4, 3, 1, 5,
            4, 4, 1, 1, 3, 5, 1, 5, 1, 4, 3, 3, 1, 1, 5, 1
        }),
        PcMorphTransitionCompletion.ProgressionThreshold);

    internal static PcMorphControllerBinding SharedHead(bool friend) => friend ? FriendHead : OtherHead;

    internal static bool TryGetCinematicMouthVariant(int actorType,
        out PcMorphCinematicMouthVariant variant)
    {
        if (CinematicMouthVariants.TryGetValue(actorType, out var matched))
        {
            variant = matched;
            return true;
        }

        variant = null!;
        return false;
    }

    private static PcMorphCinematicMouthVariant Variant(int channel, byte? forcedModuleSelector,
        params int[] sequence) => new(channel, forcedModuleSelector, Array.AsReadOnly(sequence));

    internal static PcMorphControllerFamily Identify(IReadOnlySet<uint> codeKeys)
    {
        bool factorAware = Matches(FactorAware, codeKeys);
        bool sharedHead = Matches(FriendHead, codeKeys);
        if (factorAware == sharedHead) return PcMorphControllerFamily.Unknown;
        return factorAware ? PcMorphControllerFamily.FactorAware : PcMorphControllerFamily.SharedHead;
    }

    private static bool Matches(PcMorphControllerBinding binding, IReadOnlySet<uint> codeKeys) =>
        codeKeys.Contains(binding.InitializerKey) && codeKeys.Contains(binding.UpdateKey);
}
