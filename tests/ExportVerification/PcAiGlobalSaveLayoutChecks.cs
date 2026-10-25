using System.Buffers.Binary;
using JadeView;

internal static class PcAiGlobalSaveLayoutChecks
{
    internal static void Verify()
    {
        PcAiGlobalVariableDescriptor[] descriptors =
        [
            new(0, 1, 0x21, 0, "Unsaved"),
            new(4, 1, 0x21, 0x10, "Scalar"),
            new(8, (1u << 30) | 2, 0x21, 0x30, "Flag_speciaux_sauve"),
            new(16, 1, 0x25, 0x10, "Vector")
        ];
        PcAiGlobalSaveLayout layout = PcAiGlobalSaveLayoutReader.Read(descriptors);
        Check(layout.Status == "Resolved" && layout.BufferLength == 28
                && layout.Fields.Select(field => (field.Name, field.BufferOffset, field.ByteLength))
                    .SequenceEqual(new (string, int?, int?)[]
                    {
                        ("Scalar", 0, 4),
                        ("Flag_speciaux_sauve", 4, 12),
                        ("Vector", 16, 12)
                    }),
            "The save layout should pack only Save variables in descriptor order and include array dimension words.");

        byte[] saveBuffer = new byte[layout.BufferLength!.Value];
        BinaryPrimitives.WriteInt32LittleEndian(saveBuffer.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(saveBuffer.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(saveBuffer.AsSpan(12), 0x80000001);
        Check(PcAiGlobalSaveLayoutReader.ReadIntegerArrayBit(layout, saveBuffer,
                "Flag_speciaux_sauve", 0) == true
                && PcAiGlobalSaveLayoutReader.ReadIntegerArrayBit(layout, saveBuffer,
                    "Flag_speciaux_sauve", 32) == true
                && PcAiGlobalSaveLayoutReader.ReadIntegerArrayBit(layout, saveBuffer,
                    "Flag_speciaux_sauve", 1) == false
                && PcAiGlobalSaveLayoutReader.ReadIntegerArrayBit(layout, saveBuffer,
                    "Flag_speciaux_sauve", 64) == null,
            "Saved flag bit reads should use zero-based word/bit indices and preserve out-of-range unknown state.");
        Check(PcAiGlobalSaveLayoutReader.ReadIntegerArrayBit(layout, saveBuffer[..^1],
                "Flag_speciaux_sauve", 0) == null,
            "A shorter-than-schema save buffer must remain unknown.");

        byte[] mismatchedDimensions = saveBuffer.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(mismatchedDimensions.AsSpan(4), 3);
        Check(PcAiGlobalSaveLayoutReader.ReadIntegerArrayBit(layout, mismatchedDimensions,
                "Flag_speciaux_sauve", 0) == null,
            "A runtime saved-array dimension that disagrees with the BF schema must remain unknown.");

        PcAiGlobalSaveLayout unsupported = PcAiGlobalSaveLayoutReader.Read(
            [new(0, 1, 0x7777, 0x10, "UnknownSavedType")]);
        Check(unsupported.Status == "UnsupportedLayout" && unsupported.BufferLength == null,
            "Unknown saved types must invalidate downstream offsets rather than guess their size.");

        Console.WriteLine("PASS: Iris global save layout uses ordered Save variables and bounded zero-based saved-flag reads");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
