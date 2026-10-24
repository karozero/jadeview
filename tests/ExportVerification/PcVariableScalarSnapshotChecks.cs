using System.Buffers.Binary;
using System.Text;
using JadeView;

internal static class PcVariableScalarSnapshotChecks
{
    internal static void Verify()
    {
        Check(Read(PcVariableFormat.BooleanType, 0).Value == "false (0x00000000)",
            "A zero Boolean word should remain distinguishable from an unavailable value.");
        Check(Read(PcVariableFormat.BooleanType, 0x80000000).Value == "true (0x80000000)",
            "A noncanonical true Boolean must preserve its original raw bits.");
        Check(Read(PcVariableFormat.KeyType, 0xFEDCBA98).Value == "0xFEDCBA98",
            "A serialized key must retain all 32 bits without becoming a signed decimal.");
        Check(unchecked((uint)PcScalarVariableReader.ReadOverlay(
                Payload(PcVariableFormat.KeyType, 1), Payload(PcVariableFormat.KeyType, 0xFEDCBA98),
                "value", PcVariableFormat.KeyType)) == 0xFEDCBA98,
            "Typed key overlays lost the instance word or its upper bit.");
        Check(PcScalarVariableReader.ReadOverlay(Payload(PcVariableFormat.BooleanType, 0),
                Payload(PcVariableFormat.BooleanType, 2), "value", PcVariableFormat.BooleanType) == 2,
            "Boolean storage reads must preserve noncanonical true words.");
        Check(Read(PcVariableFormat.IntegerType, unchecked((uint)-42)).Value == "-42",
            "Boolean/key support changed integer formatting.");
        Check(Read(35, 123).Value.Contains("unavailable", StringComparison.Ordinal),
            "An unknown type must not be guessed to have word storage.");
        Check(Read(PcVariableFormat.BooleanType, 1, valueOffset: 4).Value
                .Contains("unavailable", StringComparison.Ordinal),
            "A Boolean descriptor outside its storage block must not read adjacent bytes.");

        byte[] truncated = Payload(PcVariableFormat.KeyType, 1)[..^1];
        bool rejected = false;
        try { PcVariableSnapshotReader.Read(truncated, "model"); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Truncated key storage must be rejected.");
        Console.WriteLine("PASS: Boolean/key snapshots retain native word values and reject unsupported or unbounded storage");
    }

    private static CinematicScriptVariable Read(ushort type, uint value, uint valueOffset = 0) =>
        PcVariableSnapshotReader.Read(Payload(type, value, valueOffset), "model").Single();

    private static byte[] Payload(ushort type, uint value, uint valueOffset = 0)
    {
        int namesHeader = sizeof(uint) + PcVariableFormat.DescriptorSize;
        int storageHeader = namesHeader + sizeof(uint) + PcVariableFormat.NameSize;
        byte[] bytes = new byte[storageHeader + sizeof(uint) * 2];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, PcVariableFormat.DescriptorSize);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), valueOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), PcVariableFormat.ScalarLength);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), type);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(namesHeader), PcVariableFormat.NameSize);
        Encoding.ASCII.GetBytes("value").CopyTo(bytes, namesHeader + sizeof(uint));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(storageHeader), sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(storageHeader + sizeof(uint)), value);
        return bytes;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
