using JadeView;

internal static class CinematicEngineFunctionCatalogChecks
{
    internal static void Verify()
    {
        Check(0x2C26, [0, 1], "target collision-map activation bits 0x0, clear 0x1");
        Check(0x1F4D, [3, 0xFF804020], "world-table slot 3 ambient color 1 to 0xFF804020");
        Check(0x1F55, [3, 0xFF102030], "world-table slot 3 ambient color 2 to 0xFF102030");
        Check(0x213B, [2, 0], "clear light flag bit 2");
        Check(0x32CB, [1, 150], "animation track 1 time to frame 150 (2.5 seconds");
        Check(0x2F3F, [uint.MaxValue], "prefetch sound resources for all sound modifiers");
        Check(0x2F3F, [10], "prefetch sound resources for sound modifier 10");
        Check(0x30D4, [0], "assign target object to world view slot 0");
        Check(0x1FB2, [0], "reinitialize target (mode 0) and reattach it to the current world");
        Check(0x3CF4, [1], "Iris stub consumes value 1 and emits a debug trace");
        Check(0x1FD7, [0x200], "rotate target to face object 00000200 while preserving target position");
        Check(0x1FAA, [0x41400000], "rotate target around the local Y axis by 12 radians");
        Check(0x1FDC, [0x200], "attach target under object 00000200, retaining its current world transform");
        Check(0x1FDD, [], "clear target's parent-hierarchy binding");
        Check(0x1F76, [47, 2], "queue extended message 47 to the target script owner with float payload 2");
        Check(0x2714, [], "enable target dynamics and register it for the world's dynamics update");
        Check(0x2715, [], "disable target dynamics and remove it from the world's dynamics update");
        Check(0x3BC6, [0xFFFF, uint.MaxValue], "reset visibility suppression on target group and all sections");
        Check(0x1B61, [0xFF], "replace the global joystick mask with bitwise complement of 0x000000FF");
        Check(0x1FED, [8, 1], "freeze target modifier type 8");
        Check(0x2135, [0xFF804020], "set target light packed color to 0xFF804020");
        Check(0x32D5, [3, 1], "request shape 1 on target animation channel 3");
        Check(0x1FDA, [0x200], "move target to the absolute position of object 00000200");
        CheckObjectArguments(0x1FD7, [0x200]);
        CheckObjectArguments(0x1FDC, [0x200]);
        CheckObjectArguments(0x1FDA, [0x200]);
        CheckObjectArguments(0x1FDB, [0x200]);
        if (CinematicEngineFunctionCatalog.GameObjectArgumentIndices(new(1, 0x2135, [0x200])).Length != 0)
            throw new InvalidOperationException("Packed colors must not be exposed as object references.");
        Check(0x3BC7, [2, 1], "Iris stub consumes section arguments 2 and 1");
        Check(0x2F36, [], "use target object as the global microphone-position source");
        Check(0x2EF2, [4, 0x800], "request sound slot 4 on target with flags 0x00000800; Iris also starts playback immediately");
        Check(0x3AA5, [0x1234, 1, 2, 1], "multi-texture material 00001234");
        Check(0x1FE1, [0x40, 0x20], "add identity flags 0x40, remove 0x20");
        Check(0x1FC7, [0x4100], "test whether target flags2 bits 8–15 intersect mask 0x4100");
        Check(0x2713, [0x100, 0x20], "add dynamics flags 0x100, remove 0x20");
        Check(0x3D54, [uint.MaxValue, 1], "activate matching XMEN modifiers on target and its skeleton objects (all user IDs; Jaded source behavior)");

        string incomplete = CinematicEngineFunctionCatalog.Describe(new(1, 0x2C26, [1]));
        if (incomplete.Length != 0)
            throw new InvalidOperationException("A command with missing required arguments must remain undescribed.");
        string nonFiniteRotation = CinematicEngineFunctionCatalog.Describe(new(1, 0x1FAA, [0x7FC00000]));
        if (!nonFiniteRotation.Contains("non-finite serialized angle 0x7FC00000", StringComparison.Ordinal))
            throw new InvalidOperationException("Non-finite serialized angles must stay visible as raw data.");

        Console.WriteLine("PASS: source-confirmed cinematic engine command descriptions and argument guards");
    }

    private static void Check(int selector, uint[] arguments, string expectedText)
    {
        string description = CinematicEngineFunctionCatalog.Describe(new(0x12345678, selector, arguments));
        if (!description.Contains(expectedText, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Engine command {CinematicEngineFunctionCatalog.NameFor(selector)} was described as '{description}', "
                + $"expected text containing '{expectedText}'.");
    }

    private static void CheckObjectArguments(int selector, uint[] arguments)
    {
        int[] indices = CinematicEngineFunctionCatalog.GameObjectArgumentIndices(
            new(0x12345678, selector, arguments));
        if (!indices.SequenceEqual([0]))
            throw new InvalidOperationException(
                $"Engine command {CinematicEngineFunctionCatalog.NameFor(selector)} must expose its first object argument.");
    }
}
