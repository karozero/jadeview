using JadeView;

internal static class PcAiDirectFunctionCallChecks
{
    internal static void Verify()
    {
        var function = new PcSerializedAiFunction(0,
        [
            Node(0, 0x7A000AF7, 0x27, 0x01, "Type"),
            Node(1, 0, 0x1784, 0x03, "Function", "AIExecute"),
            Node(2, 20, 0x21, 0x14, "Global variable")
        ], [], [], [], []);
        var linkedNames = new Dictionary<uint, string> { [0x7A000AF7] = "univ_exec_load_new_world.ofc" };
        PcAiDirectFunctionCall[] directCalls = PcAiDirectFunctionCallAnalyzer.Analyze(function, linkedNames);
        if (directCalls.Length != 1 || directCalls[0] != new PcAiDirectFunctionCall(1, "AIExecute", 0,
                0x7A000AF7, "univ_exec_load_new_world.ofc", "ResolvedLinkedFunction"))
            throw new Exception("Direct typed function reference was not linked to its OFC resource.");

        var indirectFunction = new PcSerializedAiFunction(0,
        [
            Node(0, 20, 0x21, 0x14, "Global variable"),
            Node(1, 0, 0x1784, 0x03, "Function", "AIExecute")
        ], [], [], [], []);
        PcAiDirectFunctionCall indirect = PcAiDirectFunctionCallAnalyzer.Analyze(indirectFunction, linkedNames).Single();
        if (indirect.TargetKey != null || indirect.Status != "IndirectOrNonLiteralArgument")
            throw new Exception("An indirect AIExecute argument was incorrectly guessed as a direct function key.");

        var unknownFunction = new PcSerializedAiFunction(0,
        [
            Node(0, unchecked((int)0xDEADBEEF), 0x27, 0x01, "Type"),
            Node(1, 0, 0x1784, 0x03, "Function", "AIExecute")
        ], [], [], [], []);
        PcAiDirectFunctionCall unknown = PcAiDirectFunctionCallAnalyzer.Analyze(unknownFunction, linkedNames).Single();
        if (unknown.TargetKey != 0xDEADBEEF || unknown.TargetName != null
            || unknown.Status != "PointerRefNotLinkedByModel")
            throw new Exception("An unresolved pointerref was not preserved with its explicit status.");

        Console.WriteLine("PASS: AIExecute call-site linking requires an adjacent typed pointerref and a unique model OFC link");
    }

    private static PcAiInstruction Node(int index, int value, ushort value2, byte category, string categoryName,
        string? symbolName = null) => new(index, value, value2, 0, category, categoryName,
        symbolName, null, null);
}
