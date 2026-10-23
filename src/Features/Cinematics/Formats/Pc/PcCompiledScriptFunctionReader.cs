using System.Buffers.Binary;

namespace JadeView;

/// <summary>
/// Reads only the compact PC OFC metadata candidate observed in retail
/// containers. The candidate is supported by Iris's compiled fast path
/// (ai_common.c:1045-1100) and iristool's binarized OFC reader
/// (BFScriptingFunctionCodeChunk.java:79-101,129-141); it does not parse or
/// synthesize the omitted node body.
/// </summary>
internal static class PcCompiledScriptFunctionReader
{
    private const int HeaderSize = 12;
    private const int MaximumStackOffsetCount = 1_048_576;
    private const int MaximumDeclaredNodeBytes = 16 * 1024 * 1024;
    private const int MaximumLocalConstantBytes = 1 * 1024 * 1024;

    /// <summary>
    /// Validates [stack offset count][declared node byte size][local-constant
    /// length][local-constant bytes]. The node body is absent in this candidate.
    /// </summary>
    internal static bool TryRead(ReadOnlySpan<byte> payload,
        out PcCompiledScriptFunction? function, out string error)
    {
        function = null;
        error = string.Empty;

        if (payload.Length < HeaderSize)
            return Fail("Compact OFC payload is shorter than its 12-byte header.", out error);

        int stackOffsetCount = BinaryPrimitives.ReadInt32LittleEndian(payload);
        int declaredNodeByteSize = BinaryPrimitives.ReadInt32LittleEndian(payload[4..]);
        int localConstantLength = BinaryPrimitives.ReadInt32LittleEndian(payload[8..]);

        if (stackOffsetCount < 0 || stackOffsetCount > MaximumStackOffsetCount)
            return Fail("Stack offset count is outside the supported bounds.", out error);

        if (declaredNodeByteSize < 0 || declaredNodeByteSize > MaximumDeclaredNodeBytes
            || declaredNodeByteSize % sizeof(ulong) != 0)
            return Fail("Declared node byte size is invalid or not divisible by the 8-byte node size.", out error);

        if (localConstantLength < 0)
            return Fail("Local-constant byte length cannot be negative.", out error);

        int expectedPayloadLength;
        try
        {
            expectedPayloadLength = checked(HeaderSize + localConstantLength);
        }
        catch (OverflowException)
        {
            return Fail("Local-constant byte length overflows the compact payload size.", out error);
        }

        if (localConstantLength > MaximumLocalConstantBytes)
            return Fail("Local-constant byte length is outside the supported bounds.", out error);

        if (payload.Length != expectedPayloadLength)
            return Fail(payload.Length < expectedPayloadLength
                ? "Compact OFC local-constant block is truncated."
                : "Compact OFC payload has trailing bytes beyond its local constants.", out error);

        function = new PcCompiledScriptFunction(stackOffsetCount, declaredNodeByteSize,
            payload[HeaderSize..].ToArray());
        return true;
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
