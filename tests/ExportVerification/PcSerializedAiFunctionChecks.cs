using System.Buffers.Binary;
using System.Text;
using JadeView;

internal static class PcSerializedAiFunctionChecks
{
    internal static void Verify()
    {
        VerifyFullFunctionSectionsAndKnownNodeSymbols();
        VerifyMalformedArraysAndSectionsAreRejected();
        Console.WriteLine("PASS: full PC AI function body, raw node fields, known call/branch symbols and opaque sections");
    }

    private static void VerifyFullFunctionSectionsAndKnownNodeSymbols()
    {
        byte[] payload = CreateFunctionPayload(
        [
            new(0, 0x1789, 0, 0x03),
            new(1, 0x0900, 0, 0x02),
            new(0, 0, 0, 0x1F)
        ],
        debugNodes: [],
        localConstants: [0, 0x41, 0],
        localVariables: [(12, 3, "local_value")],
        trailing: [0xA5]);

        Require(PcSerializedAiFunctionReader.TryRead(payload, out PcSerializedAiFunction? parsed, out string error), error);
        Require(parsed is { StackOffsetCount: 8, Nodes.Length: 3, DebugNodes.Length: 0,
                LocalStringConstants.Length: 3, LocalVariables.Length: 1, TrailingBytes.Length: 1 },
            "Full serialized function sections should be retained independently.");
        Require(parsed!.Nodes[0] is { EncodedCategory: 3, EncodedValue2: 0x1789, SymbolName: "AITrackCurChangeNow" },
            "Function node should resolve its source selector name while preserving its encoded ID.");
        Require(parsed.Nodes[1].SymbolName == "JumpFalseSP"
            && parsed.Nodes[1].RelativeBranchTarget == 2
            && parsed.Nodes[1].BranchDescription!.Contains("false branch -> node 2")
            && parsed.Nodes[2].CategoryName == "End",
            "Known keyword nodes should expose source-relative branch edges without evaluating their conditions.");
        Require(parsed.LocalVariables[0] is { Offset: 12, Type: 3, Name: "local_value" }
            && parsed.TrailingBytes.SequenceEqual(new byte[] { 0xA5 }),
            "Local metadata and unrecognized trailing bytes must be preserved exactly.");
    }

    private static void VerifyMalformedArraysAndSectionsAreRejected()
    {
        byte[] misalignedMainArray = Convert.FromHexString("0800000001000000");
        byte[] truncatedNode = Convert.FromHexString("08000000080000000000000000000000");
        byte[] missingDebugLength = Convert.FromHexString("0800000000000000");
        byte[] oversizedLocalVariableBlock = CreateFunctionPayload([], [], [], []);
        BinaryPrimitives.WriteInt32LittleEndian(oversizedLocalVariableBlock.AsSpan(8), 1);

        foreach (byte[] payload in new[]
        {
            misalignedMainArray,
            truncatedNode,
            missingDebugLength,
            oversizedLocalVariableBlock
        })
        {
            Require(!PcSerializedAiFunctionReader.TryRead(payload, out PcSerializedAiFunction? parsed, out _)
                && parsed == null,
                "Malformed full AI function payload must be rejected without partial node data.");
        }
    }

    private static byte[] CreateFunctionPayload(
        (int Value, ushort Value2, byte Flags, byte Category)[] nodes,
        (int Value, ushort Value2, byte Flags, byte Category)[] debugNodes,
        byte[] localConstants,
        (int Offset, int Type, string Name)[] localVariables,
        byte[]? trailing = null)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(8);
        writer.Write(nodes.Length * 8);
        WriteNodes(writer, nodes);
        writer.Write(debugNodes.Length * 8);
        WriteNodes(writer, debugNodes);
        writer.Write(localConstants.Length);
        writer.Write(localConstants);
        if (localVariables.Length > 0 || trailing is { Length: > 0 })
        {
            writer.Write(localVariables.Length * 40);
            foreach ((int offset, int type, string name) in localVariables)
            {
                writer.Write(offset);
                writer.Write(type);
                byte[] encoded = Encoding.Latin1.GetBytes(name);
                writer.Write(encoded.AsSpan(0, Math.Min(encoded.Length, 30)));
                writer.Write(new byte[Math.Max(0, 30 - encoded.Length)]);
                writer.Write((ushort)0);
            }
        }
        if (trailing is { Length: > 0 }) writer.Write(trailing);
        return stream.ToArray();
    }

    private static void WriteNodes(BinaryWriter writer,
        (int Value, ushort Value2, byte Flags, byte Category)[] nodes)
    {
        foreach ((int value, ushort value2, byte flags, byte category) in nodes)
        {
            writer.Write(value);
            writer.Write(value2);
            writer.Write(flags);
            writer.Write(category);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
