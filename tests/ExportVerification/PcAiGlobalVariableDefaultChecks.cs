using System.Buffers.Binary;
using System.Text;
using JadeView;

internal static class PcAiGlobalVariableDefaultChecks
{
    internal static void Verify()
    {
        byte[] payload = CreateGlobalOva("Perso_ID_action", 3, [-1, 20, 30]);
        Check(PcAiGlobalVariableCatalogReader.TryRead(payload,
                out PcAiGlobalVariableDescriptor[] descriptors, out string catalogError), catalogError);
        PcAiGlobalVariableDefault[] defaults = PcAiGlobalVariableDefaultReader.Read(payload, descriptors);
        Check(defaults.Length == 1 && defaults[0].Name == "Perso_ID_action"
                && defaults[0].IntegerValues?.SequenceEqual([-1, 20, 30]) == true
                && defaults[0].ValueStatus == "Parsed",
            "Global OVA defaults should read bounded integer arrays after editor metadata and dimension headers.");
        Check(PcAiGlobalVariableDefaultReader.ReadInitializationCallbackKeys(payload)
                .SequenceEqual(new uint[] { 0x71000001u, 0, 0x71000003u, 0, 0 }),
            "OVA callback references should follow the defaults buffer in their serialized five-slot order.");

        byte[] noDefaultsPayload = CreateGlobalOva("Perso_ID_action", 12, null);
        Check(PcAiGlobalVariableCatalogReader.TryRead(noDefaultsPayload,
                out PcAiGlobalVariableDescriptor[] noDefaultsDescriptors, out string noDefaultsError), noDefaultsError);
        PcAiGlobalVariableDefault[] noDefaults = PcAiGlobalVariableDefaultReader.Read(
            noDefaultsPayload, noDefaultsDescriptors);
        Check(noDefaults.Length == 1 && noDefaults[0].ValueStatus == "NoSerializedDefaults"
                && noDefaults[0].IntegerValues == null && noDefaults[0].DefaultBufferLength == 0,
            "An empty OVA defaults buffer must be reported as absent initialization data, not an offset error.");

        bool rejectedTruncation = false;
        try
        {
            _ = PcAiGlobalVariableDefaultReader.ReadInitializationCallbackKeys(payload[..^1]);
        }
        catch (InvalidDataException)
        {
            rejectedTruncation = true;
        }
        Check(rejectedTruncation, "Truncated global OVA default data must not be read past the payload.");
        Console.WriteLine("PASS: global AI OVA defaults decode bounded arrays after Iris/Jaded editor metadata");
    }

    private static byte[] CreateGlobalOva(string name, int arrayLength, int[]? values)
    {
        const int descriptorBytes = 12;
        const int nameBytes = 30;
        int defaultsBytes = values == null ? 0 : sizeof(int) + values.Length * sizeof(int);
        byte[] payload = new byte[sizeof(int) + descriptorBytes + sizeof(int) + nameBytes
            + sizeof(int) * 3 + defaultsBytes + sizeof(uint) * 5];
        BinaryPrimitives.WriteInt32LittleEndian(payload, descriptorBytes);
        int descriptorOffset = sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(descriptorOffset), 0);
        uint packedCount = 0x40000000u | checked((uint)arrayLength);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(descriptorOffset + sizeof(int)), packedCount);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(descriptorOffset + sizeof(int) * 2),
            PcVariableFormat.IntegerType);
        int namesLengthOffset = sizeof(int) + descriptorBytes;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(namesLengthOffset), nameBytes);
        Encoding.ASCII.GetBytes(name).CopyTo(payload, namesLengthOffset + sizeof(int));
        int metadataOffset = namesLengthOffset + sizeof(int) + nameBytes;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(metadataOffset), 0);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(metadataOffset + sizeof(int)), 0);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(metadataOffset + sizeof(int) * 2), defaultsBytes);
        int defaultsOffset = metadataOffset + sizeof(int) * 3;
        if (values != null)
        {
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(defaultsOffset), arrayLength);
            for (int index = 0; index < values.Length; index++)
                BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(defaultsOffset + sizeof(int) * (index + 1)),
                    values[index]);
        }
        uint[] callbackKeys = [0x71000001, 0, 0x71000003, 0, 0];
        int callbackOffset = defaultsOffset + defaultsBytes;
        for (int callbackSlot = 0; callbackSlot < callbackKeys.Length; callbackSlot++)
            BinaryPrimitives.WriteUInt32LittleEndian(
                payload.AsSpan(callbackOffset + callbackSlot * sizeof(uint)), callbackKeys[callbackSlot]);
        return payload;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
