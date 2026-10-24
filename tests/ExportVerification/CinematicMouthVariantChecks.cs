using JadeView;

internal static class CinematicMouthVariantChecks
{
    internal static void Verify()
    {
        var expected = new Dictionary<int, (int Channel, byte? Module, int[] Forms)>
        {
            [1] = (4, null, [7, 6, 7, 6, 6, 8, 6, 7, 8, 6, 9, 6, 7, 8, 6, 9, 7, 7, 6, 6, 8, 9, 6, 7, 6, 8, 6, 9, 6, 7, 8, 6]),
            [2] = (3, null, [7, 6, 7, 6, 6, 8, 6, 7, 8, 6, 9, 6, 7, 8, 6, 9, 7, 7, 6, 6, 8, 9, 6, 7, 6, 9, 6, 6, 8, 7, 8, 6]),
            [3] = (2, null, [5, 0, 5, 0, 0, 3, 3, 0, 5, 3, 0, 4, 0, 5, 3, 3, 0, 4, 5, 5, 0, 0, 3, 3, 4, 0, 5, 0, 3, 3, 4, 0]),
            [4] = (1, 3, [1, 0, 1, 0, 0, 3, 0, 1, 3, 0, 2, 0, 1, 3, 0, 2, 1, 1, 0, 0, 3, 2, 0, 1, 0, 3, 2, 0, 0, 1, 3, 0]),
            [5] = (4, 4, [7, 6, 7, 6, 6, 8, 6, 7, 8, 6, 9, 6, 7, 8, 6, 9, 7, 7, 6, 6, 8, 9, 6, 7, 6, 9, 6, 6, 8, 8, 7, 6]),
            [6] = (4, null, [9, 0, 9, 0, 0, 10, 0, 9, 10, 0, 11, 0, 9, 10, 0, 11, 9, 9, 0, 0, 10, 11, 0, 11, 0, 9, 0, 0, 10, 10, 11, 0]),
            [7] = (4, null, [5, 0, 5, 0, 0, 4, 0, 5, 4, 0, 6, 0, 5, 4, 0, 6, 5, 5, 0, 0, 4, 6, 0, 6, 0, 5, 0, 0, 4, 4, 6, 0])
        };

        foreach (var (actorType, expectedVariant) in expected)
        {
            Require(PcMorphControllerSequences.TryGetCinematicMouthVariant(actorType, out var actual),
                $"Source lip-sync actor type {actorType} was not recognized.");
            Require(actual.Channel == expectedVariant.Channel, $"Actor type {actorType} mouth channel changed.");
            Require(actual.ForcedModuleSelector == expectedVariant.Module,
                $"Actor type {actorType} source module selector changed.");
            Require(actual.FormSequence.SequenceEqual(expectedVariant.Forms),
                $"Actor type {actorType} mouth sequence differs from Iris initialization.");
        }

        Require(!PcMorphControllerSequences.TryGetCinematicMouthVariant(0, out _),
            "Unsupported/default actor types must stay unresolved without serialized-state evidence.");
        Console.WriteLine("PASS: cinematic lip-sync source variants 1-7 retain per-type channel, module and 32-form sequence");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
