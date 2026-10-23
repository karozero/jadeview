using System.Numerics;
using System.Text;
using JadeView;

internal static class PcCollisionResourceChecks
{
    internal static void Verify()
    {
        VerifyMapShorthandAndCountedLayout();
        VerifyColSetBoundariesAndShapes();
        VerifyInstanceShorthandAndSpecificZoneLayout();
        VerifyBinaryCompactRealFixtures();
        VerifyDialectSpecificShapesAndBoundaries();
        Console.WriteLine("PASS: bounded Jaded PC COL map, instance, and ColSet readers");
    }

    private static void VerifyMapShorthandAndCountedLayout()
    {
        byte[] shorthand = U32(0x11223344);
        PcCollisionDecodeResult<PcCollisionMap> shorthandResult =
            PcCollisionResourceReader.ReadMap(shorthand, PcCollisionWireDialect.JadedPc);
        Require(shorthandResult.Success && shorthandResult.Value is
            { IsSingleCobShorthand: true, Activation: 255, CobKeys.Length: 1 }
            && shorthandResult.Value.CobKeys[0] == 0x11223344,
            "A four-byte COL map must be treated as the single-COB shorthand.");

        byte[] counted = [2, 0xA5, 0x34, 0x56, .. U32(0x12345678), .. U32(0x87654321)];
        PcCollisionDecodeResult<PcCollisionMap> countedResult =
            PcCollisionResourceReader.ReadMap(counted, PcCollisionWireDialect.JadedPc);
        Require(countedResult.Success && countedResult.Value is
            { IsSingleCobShorthand: false, Activation: 0xA5, CustomBits1: 0x34, CustomBits2: 0x56 }
            && countedResult.Value.CobKeys.SequenceEqual([0x12345678u, 0x87654321u]),
            "The counted COL map must preserve its header and ordered COB keys.");

        PcCollisionDecodeResult<PcCollisionMap> truncated =
            PcCollisionResourceReader.ReadMap(counted.AsSpan(0, counted.Length - 1), PcCollisionWireDialect.JadedPc);
        Require(truncated.Status == PcCollisionDecodeStatus.Truncated && truncated.Value is null,
            "Counted COL maps must reject key lists that exceed the resource boundary.");
    }

    private static void VerifyColSetBoundariesAndShapes()
    {
        var data = new List<byte> { 1, 0x22 };
        data.AddRange(U16(0x1234));
        data.AddRange(Enumerable.Range(0, 16).Select(value => (byte)value));
        data.AddRange([0x01, 0x01, 0x05, 0x06]); // Zone flags, box type, bone, design.
        data.AddRange(U32(3));
        data.AddRange(Encoding.ASCII.GetBytes("col"));
        data.Add(7); // AI index.
        AddVector(data, new Vector3(4, 5, 6)); // Jaded serializes max before min.
        AddVector(data, new Vector3(-4, -5, -6));

        PcCollisionDecodeResult<PcCollisionSet> result = PcCollisionResourceReader.ReadSet(
            [.. data], PcCollisionWireDialect.JadedPc);
        Require(result.Success && result.Value is { Flags: 0x22, SavedInstanceCount: 0x1234 }
            && result.Value.AiIndexTranslation.SequenceEqual(Enumerable.Range(0, 16).Select(value => (byte)value))
            && result.Value.Zones is [{ NameBytes.Length: 3, AiIndex: 7, Shape.Kind: PcCollisionShapeKind.Box } zone]
            && zone.NameBytes.SequenceEqual("col"u8.ToArray())
            && zone.Shape.FirstVector == new Vector3(4, 5, 6)
            && zone.Shape.SecondVector == new Vector3(-4, -5, -6),
            "ColSet must use serialized count/translation/zone fields, not runtime struct sizes.");

        PcCollisionDecodeResult<PcCollisionSet> truncated = PcCollisionResourceReader.ReadSet(
            data.Take(data.Count - 2).ToArray(), PcCollisionWireDialect.JadedPc);
        Require(truncated.Status == PcCollisionDecodeStatus.Truncated && truncated.Value is null,
            "ColSet shape data must be bounded by the supplied resource bytes.");
    }

    private static void VerifyInstanceShorthandAndSpecificZoneLayout()
    {
        PcCollisionDecodeResult<PcCollisionInstance> shorthand = PcCollisionResourceReader.ReadInstance(
            U32(0xAABBCCDD), PcCollisionWireDialect.JadedPc);
        Require(shorthand.Success && shorthand.Value is { IsColSetOnlyShorthand: true,
                ColSetKey: 0xAABBCCDD, ActivationMask: ushort.MaxValue },
            "A four-byte COL instance must retain its ColSet key as the shorthand form.");

        var data = new List<byte>();
        data.AddRange(U32(0x10203040));
        data.AddRange([1, 0, 1, 0xFF]); // Total, shared, specific, signed priority.
        data.Add(0); // Selected ColSet zone index.
        data.AddRange([0x03, 0x02, 0x04, 0x05, 0x00]); // Specific zone fields and ColSet index.
        data.AddRange(U32(0)); // Empty name.
        data.Add(9); // AI index.
        AddVector(data, new Vector3(1, 2, 3));
        data.AddRange(F32(1.5f));
        data.AddRange(U16(0x1234)); // Activation mask.
        data.AddRange(U16(0x0001)); // Specific mask.
        data.AddRange(U16(0x9999)); // Legacy serialized field.
        data.AddRange(U16(0x5678)); // Crossable.
        data.AddRange(U16(0xABCD)); // Instance flags.
        data.AddRange([8, 2]); // Max/min LOD.

        PcCollisionDecodeResult<PcCollisionInstance> result = PcCollisionResourceReader.ReadInstance(
            [.. data], PcCollisionWireDialect.JadedPc);
        Require(result.Success && result.Value is
            { ColSetKey: 0x10203040, ZoneCount: 1, SharedZoneCount: 0, SpecificZoneCount: 1,
              Priority: -1, ActivationMask: 0x1234, SpecificMask: 1, LegacyField: 0x9999,
              SerializedCrossableField: 0x5678, EffectiveCrossable: 0x5678,
              Flags: 0xABCD, MaxLod: 8, MinLod: 2 }
            && result.Value.SelectedZoneIndices.SequenceEqual(new byte[] { 0 })
            && result.Value.SpecificZones is [{ ColSetZoneIndex: 0, AiIndex: 9,
                Shape.Kind: PcCollisionShapeKind.Sphere, Shape.Radius: 1.5f }],
            "COL instance parsing must preserve its variable specific zone and serialized tail fields.");

        PcCollisionDecodeResult<PcCollisionInstance> truncated = PcCollisionResourceReader.ReadInstance(
            data.Take(data.Count - 1).ToArray(), PcCollisionWireDialect.JadedPc);
        Require(truncated.Status == PcCollisionDecodeStatus.Truncated && truncated.Value is null,
            "COL instance parsing must reject a truncated LOD tail.");
    }

    private static void VerifyBinaryCompactRealFixtures()
    {
        // Exact resource bytes copied from the FF005520 audit; no full game archive is retained.
        byte[] instanceBytes = Convert.FromHexString(
            "4C68006C060402000101020304006402FF001100000000000000000000000000003FCDCC4C3F" +
            "61021A051100000000000000000000006666E63E6666E63EFFFF21009A5804000201");
        PcCollisionDecodeResult<PcCollisionInstance> instance = PcCollisionResourceReader.ReadInstance(
            instanceBytes, PcCollisionWireDialect.JadedPc, PcCollisionReadMode.BinaryCompact);
        Require(instance.Success && instance.Value is
            { ColSetKey: 0x6C00684C, ZoneCount: 6, SharedZoneCount: 4, SpecificZoneCount: 2,
              SpecificZones.Length: 2, ActivationMask: 0xFFFF, SpecificMask: 0x0021,
              SerializedCrossableField: 0x589A, EffectiveCrossable: 0x589A, Flags: 0x0004,
              MaxLod: 2, MinLod: 1 }
            && instance.Value.SpecificZones[0].Shape is
                { Kind: PcCollisionShapeKind.Sphere, Radius: 0.8f }
            && instance.Value.SpecificZones[1].Shape is
                { Kind: PcCollisionShapeKind.Sphere, Radius: 0.45f }
            && instance.Warnings.Length == 1,
            "FF005520's binary compact collision instance must parse when editor-only fields are explicitly omitted.");

        byte[] setBytes = Convert.FromHexString(
            "0100FFFFFFFFFFFF00FFFFFFFFFFFFFFFFFF0402C6000D0000000000000000000000000000000000803F");
        PcCollisionDecodeResult<PcCollisionSet> set = PcCollisionResourceReader.ReadSet(
            setBytes, PcCollisionWireDialect.JadedPc, PcCollisionReadMode.BinaryCompact);
        Require(set.Success && set.Value is
            { Flags: 0, SavedInstanceCount: null, Zones.Length: 1,
              AiIndexTranslation.Length: 16 }
            && set.Value.Zones[0] is
                { Flags: 0x04, Type: 0x02, BoneIndex: 0xC6, Design: 0,
                  Shape.Kind: PcCollisionShapeKind.Sphere, AiIndex: 0 }
            && set.Value.Zones[0].Shape.Radius == 1.0f,
            "FF005520's 42-byte ColSet must parse with its two-byte header and omitted editor fields.");

        PcCollisionDecodeResult<PcCollisionInstance> richMode = PcCollisionResourceReader.ReadInstance(
            instanceBytes, PcCollisionWireDialect.JadedPc, PcCollisionReadMode.EditorRich);
        Require(!richMode.Success,
            "The same binary fixture must not be silently reinterpreted as an editor-rich resource.");
    }

    private static void VerifyDialectSpecificShapesAndBoundaries()
    {
        var cylinderData = new List<byte> { 1, 0 };
        cylinderData.AddRange(U16(0));
        cylinderData.AddRange(new byte[16]);
        cylinderData.AddRange([0, 3, 0, 0]);
        cylinderData.AddRange(U32(0));
        cylinderData.Add(0);
        AddVector(cylinderData, new Vector3(1, 2, 3));
        cylinderData.AddRange(F32(4)); // Iris serializes cylinder height before radius.
        cylinderData.AddRange(F32(5));
        PcCollisionDecodeResult<PcCollisionSet> irisCylinder = PcCollisionResourceReader.ReadSet(
            [.. cylinderData], PcCollisionWireDialect.IrisCompact);
        Require(irisCylinder.Success && irisCylinder.Value?.Zones[0].Shape is
            { Kind: PcCollisionShapeKind.Cylinder, Height: 4, Radius: 5 },
            "Iris cylinder payload order must follow its loader (height, then radius).");

        PcCollisionDecodeResult<PcCollisionSet> irisUnknown = PcCollisionResourceReader.ReadSet(
            BuildNoPayloadUnknownShapeSet(5), PcCollisionWireDialect.IrisCompact);
        Require(irisUnknown.Success && irisUnknown.Value?.Zones[0].Shape is
            { Kind: PcCollisionShapeKind.UnsupportedNoPayload, UnsupportedType: 5 }
            && irisUnknown.Warnings.Length == 1,
            "Iris's source switch has no payload branch for unknown shape types, so retain them as no-payload with a warning.");

        PcCollisionDecodeResult<PcCollisionSet> oversizedCount = PcCollisionResourceReader.ReadSet(
            [17, 0, 0, 0], PcCollisionWireDialect.JadedPc);
        Require(oversizedCount.Status == PcCollisionDecodeStatus.InvalidData,
            "ColSet zone counts beyond the source-defined 16-zone limit must be rejected.");

        var unknownShape = new List<byte> { 1, 0, 0, 0 };
        unknownShape.AddRange(new byte[16]);
        unknownShape.AddRange([0, 5, 0, 0]);
        unknownShape.AddRange(U32(0));
        unknownShape.Add(0);
        PcCollisionDecodeResult<PcCollisionSet> unsupported = PcCollisionResourceReader.ReadSet(
            [.. unknownShape], PcCollisionWireDialect.JadedPc);
        Require(unsupported.Success && unsupported.Value?.Zones[0].Shape is
            { Kind: PcCollisionShapeKind.UnsupportedNoPayload, UnsupportedType: 5 }
            && unsupported.Warnings.Any(warning => warning.Contains("type 5", StringComparison.OrdinalIgnoreCase)),
            "Unknown Jaded shapes must retain their type and report unsupported geometry; the loader and saver have no payload branch.");

        byte[] negativeRadius = BuildNoPayloadUnknownShapeSet(2);
        negativeRadius[^1] = 0xBF; // radius float becomes negative.
        PcCollisionDecodeResult<PcCollisionSet> invalidRadius = PcCollisionResourceReader.ReadSet(
            negativeRadius, PcCollisionWireDialect.JadedPc);
        Require(invalidRadius.Status == PcCollisionDecodeStatus.InvalidData,
            "A serialized negative radius must be invalid data, not a truncated shape.");

        PcCollisionDecodeResult<PcCollisionSet> truncatedHeader = PcCollisionResourceReader.ReadSet(
            [1, 0, 0], PcCollisionWireDialect.JadedPc);
        Require(truncatedHeader.Status == PcCollisionDecodeStatus.Truncated,
            "Short ColSet headers must be reported as truncated.");
    }

    private static byte[] BuildNoPayloadUnknownShapeSet(byte type)
    {
        var data = new List<byte> { 1, 0 };
        data.AddRange(U16(0));
        data.AddRange(new byte[16]);
        data.AddRange([0, type, 0, 0]);
        data.AddRange(U32(0));
        data.Add(0);
        if (type == 2)
        {
            AddVector(data, Vector3.Zero);
            data.AddRange(F32(1));
        }
        return [.. data];
    }

    private static byte[] U16(ushort value) =>
        [unchecked((byte)value), unchecked((byte)(value >> 8))];

    private static byte[] U32(uint value) =>
        [unchecked((byte)value), unchecked((byte)(value >> 8)),
         unchecked((byte)(value >> 16)), unchecked((byte)(value >> 24))];

    private static byte[] F32(float value) => U32(BitConverter.SingleToUInt32Bits(value));

    private static void AddVector(List<byte> target, Vector3 value)
    {
        target.AddRange(F32(value.X));
        target.AddRange(F32(value.Y));
        target.AddRange(F32(value.Z));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
