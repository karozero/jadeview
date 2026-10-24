using System.Buffers.Binary;
using System.Text;

namespace JadeView;

/// <summary>
/// Reads the full serialized PC AI-function layout used by Iris/Jaded. It only decodes records;
/// stack behavior, callback dispatch, and runtime branch conditions are not executed.
/// </summary>
internal static class PcSerializedAiFunctionReader
{
    private const int FunctionHeaderSize = sizeof(int) * 2;
    private const int NodeSize = 8;
    private const int LocalVariableSize = 40;
    private const int MaximumStackOffsetCount = 1_048_576;
    private const int MaximumNodeBytes = 16 * 1024 * 1024;
    private const int MaximumAuxiliaryBytes = 16 * 1024 * 1024;
    private const int MaximumLocalVariables = 262_144;

    internal static bool TryRead(ReadOnlySpan<byte> payload,
        out PcSerializedAiFunction? function, out string error)
    {
        function = null;
        error = string.Empty;
        if (payload.Length < FunctionHeaderSize)
            return Fail("Serialized AI function is shorter than its stack/node-size header.", out error);

        int stackOffsetCount = BinaryPrimitives.ReadInt32LittleEndian(payload);
        int mainNodeBytes = BinaryPrimitives.ReadInt32LittleEndian(payload[sizeof(int)..]);
        if (stackOffsetCount < 0 || stackOffsetCount > MaximumStackOffsetCount)
            return Fail("Serialized local-stack size is outside supported bounds.", out error);
        if (!IsBoundedCount(mainNodeBytes, MaximumNodeBytes, NodeSize))
            return Fail("Main AI node byte count is invalid or misaligned.", out error);

        int cursor = FunctionHeaderSize;
        if (!TryReadBytes(payload, ref cursor, mainNodeBytes, out ReadOnlySpan<byte> mainNodes))
            return Fail("Main AI node array is truncated.", out error);

        if (!TryReadInt32(payload, ref cursor, out int debugNodeBytes)
            || !IsBoundedCount(debugNodeBytes, MaximumNodeBytes, NodeSize)
            || !TryReadBytes(payload, ref cursor, debugNodeBytes, out ReadOnlySpan<byte> debugNodes))
            return Fail("Debug AI node array length is invalid, misaligned or truncated.", out error);

        if (!TryReadInt32(payload, ref cursor, out int constantBytes)
            || !IsBoundedCount(constantBytes, MaximumAuxiliaryBytes, 1)
            || !TryReadBytes(payload, ref cursor, constantBytes, out ReadOnlySpan<byte> localConstants))
            return Fail("Local string-constant block is invalid or truncated.", out error);

        var localVariables = Array.Empty<PcAiLocalVariable>();
        if (cursor < payload.Length)
        {
            if (!TryReadInt32(payload, ref cursor, out int localVariableBytes)
                || !IsBoundedCount(localVariableBytes,
                    Math.Min(MaximumAuxiliaryBytes, MaximumLocalVariables * LocalVariableSize), LocalVariableSize)
                || !TryReadBytes(payload, ref cursor, localVariableBytes, out ReadOnlySpan<byte> localVariablePayload))
                return Fail("Local-variable metadata block is invalid or truncated.", out error);
            localVariables = ReadLocalVariables(localVariablePayload);
        }

        function = new(stackOffsetCount,
            ReadNodes(mainNodes), ReadNodes(debugNodes), localConstants.ToArray(),
            localVariables, payload[cursor..].ToArray());
        return true;
    }

    private static PcAiInstruction[] ReadNodes(ReadOnlySpan<byte> bytes)
    {
        var nodes = new PcAiInstruction[bytes.Length / NodeSize];
        for (int index = 0; index < nodes.Length; index++)
        {
            int offset = index * NodeSize;
            int value = BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
            ushort encodedFunctionId = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(offset + 4)..]);
            byte flags = bytes[offset + 6];
            byte encodedCategory = bytes[offset + 7];
            string categoryName = PcAiNodeCatalog.CategoryName(encodedCategory);
            string? symbolName = PcAiNodeCatalog.SymbolName(encodedCategory, encodedFunctionId);
            (int? target, string? branchDescription) = ResolveBranch(index, value,
                encodedCategory, encodedFunctionId, nodes.Length);
            nodes[index] = new(index, value, encodedFunctionId, flags, encodedCategory,
                categoryName, symbolName, target, branchDescription);
        }
        return nodes;
    }

    private static (int? Target, string? Description) ResolveBranch(int nodeIndex, int value,
        byte category, ushort functionId, int nodeCount)
    {
        if (category != 2) return (null, null);
        int target;
        string branch;
        switch (functionId)
        {
            case 0x08FC: // AI_EvalKeyword_Jump
                target = nodeIndex + value;
                branch = $"unconditional jump to node {target}";
                break;
            case 0x08FD: // AI_EvalKeyword_JumpFalse
            case 0x0900: // AI_EvalKeyword_JumpFalseSP
                target = nodeIndex + value;
                branch = $"false branch -> node {target}; true branch -> node {nodeIndex + 1}";
                break;
            case 0x08FE: // AI_EvalKeyword_JumpTrue
            case 0x0901: // AI_EvalKeyword_JumpTrueSP
                target = nodeIndex + value;
                branch = $"true branch -> node {target}; false branch -> node {nodeIndex + 1}";
                break;
            default:
                return (null, null);
        }

        return target >= 0 && target < nodeCount
            ? (target, branch)
            : (null, $"relative branch target {target} is outside the {nodeCount}-node function");
    }

    private static PcAiLocalVariable[] ReadLocalVariables(ReadOnlySpan<byte> bytes)
    {
        int count = bytes.Length / LocalVariableSize;
        if (count > MaximumLocalVariables)
            throw new InvalidDataException("Local-variable metadata count is outside supported bounds.");
        var variables = new PcAiLocalVariable[count];
        for (int index = 0; index < count; index++)
        {
            int offset = index * LocalVariableSize;
            int variableOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
            int type = BinaryPrimitives.ReadInt32LittleEndian(bytes[(offset + sizeof(int))..]);
            ReadOnlySpan<byte> encodedName = bytes.Slice(offset + sizeof(int) * 2, 30);
            int nullIndex = encodedName.IndexOf((byte)0);
            if (nullIndex >= 0) encodedName = encodedName[..nullIndex];
            variables[index] = new(variableOffset, type, Encoding.Latin1.GetString(encodedName).Trim());
        }
        return variables;
    }

    private static bool IsBoundedCount(int count, int maximum, int alignment) =>
        count >= 0 && count <= maximum && count % alignment == 0;

    private static bool TryReadInt32(ReadOnlySpan<byte> bytes, ref int cursor, out int value)
    {
        if (cursor < 0 || cursor > bytes.Length - sizeof(int))
        {
            value = 0;
            return false;
        }
        value = BinaryPrimitives.ReadInt32LittleEndian(bytes[cursor..]);
        cursor += sizeof(int);
        return true;
    }

    private static bool TryReadBytes(ReadOnlySpan<byte> bytes, ref int cursor, int count,
        out ReadOnlySpan<byte> result)
    {
        if (cursor < 0 || count < 0 || cursor > bytes.Length - count)
        {
            result = default;
            return false;
        }
        result = bytes.Slice(cursor, count);
        cursor += count;
        return true;
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
