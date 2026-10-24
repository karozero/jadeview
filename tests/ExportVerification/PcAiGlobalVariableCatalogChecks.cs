using System.Buffers.Binary;
using System.Text;
using JadeView;

internal static class PcAiGlobalVariableCatalogChecks
{
    internal static void Verify()
    {
        byte[] payload = BuildPayload();
        if (!PcAiGlobalVariableCatalogReader.TryRead(payload, out PcAiGlobalVariableDescriptor[] variables,
            out string error))
            throw new Exception(error);
        if (variables.Length != 2 || variables[0] != new PcAiGlobalVariableDescriptor(0, 1, 0x21, 0x10, "load_world")
            || variables[1].Offset != 24 || variables[1].ArrayDimensions != 1
            || variables[1].ArrayLength != 3 || variables[1].Name != "WORLD_Key_Map")
            throw new Exception("Global AI variable names, offsets, types or packed array bounds were not decoded.");

        byte[] malformed = payload.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(malformed, 13);
        if (PcAiGlobalVariableCatalogReader.TryRead(malformed, out _, out _))
            throw new Exception("Misaligned global AI variable descriptors were accepted.");
        int nameTableEnd = sizeof(int) + 2 * 12 + sizeof(int) + 2 * 30;
        if (PcAiGlobalVariableCatalogReader.TryRead(payload[..(nameTableEnd - 1)], out _, out _))
            throw new Exception("Truncated global AI variable names were accepted.");
        Console.WriteLine("PASS: source-layout global AI variable offsets, names, types, arrays and bounds");
    }

    private static byte[] BuildPayload()
    {
        byte[] payload = new byte[4 + 2 * 12 + 4 + 2 * 30 + 8];
        BinaryPrimitives.WriteInt32LittleEndian(payload, 2 * 12);
        WriteDescriptor(4, 0, 1, 0x21, 0x10);
        WriteDescriptor(16, 24, (1u << 30) | 3, 0x2D, 0);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4 + 2 * 12), 2 * 30);
        Encoding.ASCII.GetBytes("load_world").CopyTo(payload, 4 + 2 * 12 + 4);
        Encoding.ASCII.GetBytes("WORLD_Key_Map").CopyTo(payload, 4 + 2 * 12 + 4 + 30);
        return payload;

        void WriteDescriptor(int offset, int variableOffset, uint packedCount, ushort type, ushort flags)
        {
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset), variableOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset + 4), packedCount);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset + 8), type);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset + 10), flags);
        }
    }
}
