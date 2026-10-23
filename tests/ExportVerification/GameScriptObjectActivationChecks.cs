using JadeView;

internal static class GameScriptObjectActivationChecks
{
    internal static void Verify()
    {
        uint[] relevantBits = [1, 0x10, 0x00400000, 0x04000000, 0x00200000];
        for (int combination = 0; combination < 1 << relevantBits.Length; combination++)
        {
            uint flags = 0;
            for (int bitIndex = 0; bitIndex < relevantBits.Length; bitIndex++)
                if ((combination & (1 << bitIndex)) != 0) flags |= relevantBits[bitIndex];
            foreach (bool specialTime in new[] { false, true })
            {
                // Direct translation of the source predicate, independent from helper constants.
                bool sourceAddsObject = (flags & 1) == 0 && (specialTime
                    ? (flags & 0x04000000) != 0
                    : (flags & 0x04400010) == 0);
                bool evaluatedAddsObject = GameScriptObjectActivation.Evaluate(flags, specialTime)
                    == GameScriptObjectActivationResult.Eligible;
                if (sourceAddsObject != evaluatedAddsObject)
                    throw new InvalidOperationException($"Activation diverged for {flags:X8}, special={specialTime}.");
            }
        }
        if (GameScriptObjectActivation.Evaluate(0, null) != GameScriptObjectActivationResult.TimeModeUnknown
            || GameScriptObjectActivation.Evaluate(1, null) != GameScriptObjectActivationResult.AlreadyActive)
            throw new InvalidOperationException("Unknown time must not imply a normal-time dispatch or duplicate table insertion.");
        Console.WriteLine("PASS: Iris object activation matches source masks across normal/special time and preserves unknown mode");
    }
}
