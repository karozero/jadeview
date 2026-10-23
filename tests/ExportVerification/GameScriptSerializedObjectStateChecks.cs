using JadeView;

internal static class GameScriptSerializedObjectStateChecks
{
    internal static void Verify()
    {
        // Independently enumerate the serialized bits that Iris retains or restores.
        int[] retainedBits = [7, 8, 9, 10, 11, 12, 13, 14, 15, 18, 19, 20, 23, 24, 25, 27, 28, 29, 30];
        for (int bitIndex = 0; bitIndex < 32; bitIndex++)
        {
            uint serializedBit = 1u << bitIndex;
            uint expected = retainedBits.Contains(bitIndex) ? serializedBit : 0;
            if (bitIndex == 29) expected |= 0x00200000;
            if (bitIndex == 30) expected |= 0x00400000;
            Require(GameScriptSerializedObjectState.ProjectInitialFlags2(serializedBit) == expected,
                $"Serialized bit {bitIndex} did not follow Iris initial flags2 projection.");
        }

        Require(GameScriptSerializedObjectState.ProjectInitialFlags2(uint.MaxValue) == 0x7BFCFF80,
            "Combined serialized state lost retained flags, translated force bits or custom bits.");
        Require(GameScriptSerializedObjectState.ProjectInitialFlags2(0) == 0,
            "An empty serialized state invented runtime activity or detection-list flags.");
        Console.WriteLine("PASS: serialized GAO state projects Iris flags2 bits without inventing runtime activity");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
