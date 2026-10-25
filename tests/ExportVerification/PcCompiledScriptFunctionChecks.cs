using System.Buffers.Binary;
using JadeView;

internal static class PcCompiledScriptFunctionChecks
{
    internal static void Verify()
    {
        VerifyRetailCineEventsInitHeader();
        VerifyRetailCineEventsPlayTrackHeader();
        VerifyBinaryLocalConstants();
        VerifyMalformedPayloadsAreRejected();
        VerifyModelScopedFunctionResourceResolution();
        Console.WriteLine("PASS: bounded full-body/compact-metadata PC OFC layouts and malformed payload checks");
    }

    private static void VerifyRetailCineEventsInitHeader()
    {
        // Raw first 12 bytes from kind-'r' key 350091C2 in decoded retail FF005833.
        byte[] payload = CreatePayload(240, "0C000000D80A0000E4000000");
        payload[12] = 0x28; // First observed byte of the local-constant block.
        Require(PcCompiledScriptFunctionReader.TryRead(payload, out var parsed, out var error), error);
        Require(parsed is { StackOffsetCount: 12, DeclaredNodeByteSize: 2776 }
            && parsed.LocalConstantBytes.Length == 228
            && parsed.LocalConstantBytes[0] == 0x28
            && parsed.InterpretationStatus == "CompactCompiledLayoutCandidate",
            "Retail C2 compact candidate fields or local-constant preservation changed.");
    }

    private static void VerifyRetailCineEventsPlayTrackHeader()
    {
        // Raw first 12 bytes from kind-'r' key 350091C9 in decoded retail FF005833.
        byte[] payload = CreatePayload(12, "54000000B804000000000000");
        Require(PcCompiledScriptFunctionReader.TryRead(payload, out var parsed, out var error), error);
        Require(parsed is { StackOffsetCount: 84, DeclaredNodeByteSize: 1208 }
            && parsed.LocalConstantBytes.Length == 0,
            "Retail C9 compact candidate fields or empty metadata block changed.");
    }

    private static void VerifyBinaryLocalConstants()
    {
        byte[] payload = Convert.FromHexString("0100000010000000040000000041FF00");
        Require(PcCompiledScriptFunctionReader.TryRead(payload, out var parsed, out string error), error);
        Require(parsed!.LocalConstantBytes.SequenceEqual(new byte[] { 0, 0x41, 0xFF, 0 }),
            "Local constants must preserve embedded NULs and non-text bytes without string decoding.");
    }

    private static void VerifyMalformedPayloadsAreRejected()
    {
        byte[] validHeader = Convert.FromHexString("0C000000D80A0000E4000000");
        byte[] truncatedHeader = validHeader[..11];
        byte[] misalignedNodeSize = CreatePayload(12, "0C0000000100000000000000");
        byte[] negativeNodeSize = CreatePayload(12, "0C000000F8FFFFFF00000000");
        byte[] excessiveNodeSize = CreatePayload(12, "0C0000000000000200000000");
        byte[] truncatedConstants = CreatePayload(20, "0C000000D80A0000E4000000");
        byte[] trailingByte = CreatePayload(13, "54000000B804000000000000");
        byte[] negativeStackCount = CreatePayload(12, "FFFFFFFFB804000000000000");
        byte[] negativeConstantLength = CreatePayload(12, "54000000B8040000FFFFFFFF");
        byte[] excessiveConstantLength = CreatePayload(12, "54000000B804000001001000");

        foreach (byte[] malformed in new[]
        {
            truncatedHeader,
            misalignedNodeSize,
            negativeNodeSize,
            excessiveNodeSize,
            truncatedConstants,
            trailingByte,
            negativeStackCount,
            negativeConstantLength,
            excessiveConstantLength
        })
        {
            Require(!PcCompiledScriptFunctionReader.TryRead(malformed, out var parsed, out _)
                && parsed == null,
                "Malformed compact OFC input was accepted.");
        }

        byte[] overflowConstants = CreatePayload(12, "54000000B8040000FFFFFF7F");
        Require(!PcCompiledScriptFunctionReader.TryRead(overflowConstants, out _, out _),
            "An overflowing compact OFC local-constant byte length was accepted.");
    }

    private static void VerifyModelScopedFunctionResourceResolution()
    {
        const uint modelWorldKey = 0x10000001;
        const uint functionKey = 0x20000001;
        const uint wrongKindKey = 0x20000002;
        const uint missingKey = 0x20000003;
        const uint malformedKey = 0x20000004;
        const uint ambiguousKey = 0x20000005;

        var model = new PcSceneLoader.Record(0x30000001, 's', 0, 0)
        {
            LoadWorldKey = modelWorldKey,
            References =
            [new(functionKey, 'r'), new(0x20000006, 'r'), new(wrongKindKey, 'r'), new(missingKey, 'r'),
             new(malformedKey, 'r'), new(ambiguousKey, 'r')]
        };
        byte[] validPayload = Convert.FromHexString("010000001000000000000000");
        byte[] unsupportedPayload = Convert.FromHexString("01000000100000000000000000");
        byte[] alternateWorldPayload = Convert.FromHexString("020000001800000000000000");
        byte[] serializedFunctionPayload = CreateSerializedFunctionPayload();

        var localFunction = CreateRecord(functionKey, 'r', modelWorldKey, validPayload, "local.ofc");
        var serializedFunction = CreateRecord(0x20000006, 'r', modelWorldKey,
            serializedFunctionPayload, "serialized.ofc");
        var shadowedFunction = CreateRecord(functionKey, 'r', 0x10000002, alternateWorldPayload, "other.ofc");
        var wrongKind = CreateRecord(wrongKindKey, 'c', modelWorldKey, [], "not-ofc");
        var malformed = CreateRecord(malformedKey, 'r', modelWorldKey, unsupportedPayload, "malformed.ofc");
        var ambiguousFirst = CreateRecord(ambiguousKey, 'r', 0x10000002, alternateWorldPayload, "ambiguous-a.ofc");
        var ambiguousSecond = CreateRecord(ambiguousKey, 'r', 0x10000003, alternateWorldPayload, "ambiguous-b.ofc");
        var records = new[] { model, localFunction, serializedFunction, shadowedFunction, wrongKind, malformed,
            ambiguousFirst, ambiguousSecond };
        var origins = new Dictionary<PcSceneLoader.Record, byte[]>
        {
            [localFunction] = validPayload,
            [serializedFunction] = serializedFunctionPayload,
            [shadowedFunction] = alternateWorldPayload,
            [wrongKind] = [],
            [malformed] = unsupportedPayload,
            [ambiguousFirst] = alternateWorldPayload,
            [ambiguousSecond] = alternateWorldPayload
        };

        var resources = PcScriptFunctionResourceReader.Read(model,
            new PcSceneResourceIndex(records), origins).ToDictionary(resource => resource.FunctionKey);

        Require(resources[functionKey].Status == "CompactMetadataCandidate"
            && resources[functionKey].WorldKey == modelWorldKey
            && resources[functionKey].Metadata is { StackOffsetCount: 1, DeclaredNodeByteSize: 16 },
            "A model-local .ofc reference should resolve in model scope and expose metadata only.");
        Require(resources[0x20000006].Status == "SerializedBody"
            && resources[0x20000006].Metadata == null
            && resources[0x20000006].SerializedFunction is { StackOffsetCount: 2, Nodes.Length: 1 },
            "A full model-local .ofc body should be decoded before trying the compact metadata layout.");
        Require(resources[wrongKindKey].Status == "WrongResourceKind"
            && resources[missingKey].Status == "MissingResource"
            && resources[malformedKey].Status == "UnsupportedLayout"
            && resources[ambiguousKey].Status == "AmbiguousResource",
            "Wrong-kind, missing, malformed and ambiguous .ofc references should remain diagnosable.");

        static PcSceneLoader.Record CreateRecord(uint key, char kind, uint worldKey,
            byte[] payload, string name) => new(key, kind, 0, payload.Length)
        {
            LoadWorldKey = worldKey,
            Name = name
        };

        static byte[] CreateSerializedFunctionPayload()
        {
            byte[] payload = new byte[sizeof(int) * 4 + sizeof(ulong)];
            BinaryPrimitives.WriteInt32LittleEndian(payload, 2);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(sizeof(int)), sizeof(ulong));
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(sizeof(int) * 2), 0x1234);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(sizeof(int) * 2 + sizeof(int)), 0x1785);
            payload[sizeof(int) * 2 + sizeof(int) + sizeof(ushort)] = 1;
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(sizeof(int) * 2 + sizeof(ulong)), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(sizeof(int) * 3 + sizeof(ulong)), 0);
            return payload;
        }
    }

    private static byte[] CreatePayload(int size, string headerHex)
    {
        byte[] header = Convert.FromHexString(headerHex);
        var payload = new byte[size];
        header.CopyTo(payload, 0);
        return payload;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
