using System.Buffers.Binary;
using JadeView;

internal static class PcLightChecks
{
    internal static void Verify()
    {
        VerifyValidPayloadAndRawFields();
        VerifyOnlyKnownTypeCodesAreNamed();
        VerifyMalformedPayloadsAreRejected();
        VerifyGraphicEnvelope();
        Console.WriteLine("PASS: bounded PC light payload layout, raw flags/reference retention and malformed input checks");
    }

    private static void VerifyValidPayloadAndRawFields()
    {
        const uint flags = 0xA5C312A2;
        const uint color = 0x80F02010;
        const uint lightGaoKey = 0x62001234;
        byte[] payload = CreatePayload(flags, color, 1.25f, 12.5f, 0.375f, 0.625f, lightGaoKey);

        Require(PcLightReader.TryRead(payload, out var light, out var error), error);
        SceneLight parsedLight = light ?? throw new InvalidOperationException("A valid 28-byte light body did not produce a model.");
        Require(parsedLight.Flags == flags && parsedLight.Color == color && parsedLight.LightGaoKey == lightGaoKey,
            "The light flags, packed color, or referenced GAO key were changed.");
        Require(parsedLight.Near == 1.25f && parsedLight.Far == 12.5f && parsedLight.Density == 0.375f && parsedLight.I8 == 0.625f,
            "The four serialized light floats were read from the wrong offsets or byte order.");
        Require(parsedLight.HasLightGaoReference && parsedLight.TypeCode == (flags & SceneLight.TypeMask),
            "The reference sentinel or raw low type bits were not retained.");

        byte[] noReferencePayload = CreatePayload(0, 0, 0, 0, 0, 0, SceneLight.NoLightGaoKey);
        Require(PcLightReader.TryRead(noReferencePayload, out var noReference, out error), error);
        Require(noReference is { HasLightGaoReference: false, KnownType: SceneLightType.Omni },
            "The native -1 light-GAO reference sentinel or omni type was not represented.");
    }

    private static void VerifyOnlyKnownTypeCodesAreNamed()
    {
        SceneLightType[] expectedTypes =
        [
            SceneLightType.Omni,
            SceneLightType.Direct,
            SceneLightType.Spot,
            SceneLightType.Fog,
            SceneLightType.AddMaterial
        ];

        for (uint typeCode = 0; typeCode < expectedTypes.Length; typeCode++)
        {
            var light = new SceneLight(0xFFFF_FFF8 | typeCode, 0, 0, 0, 0, 0, SceneLight.NoLightGaoKey);
            Require(light.KnownType == expectedTypes[typeCode],
                $"Source-backed light type code {typeCode} was not mapped correctly.");
            Require(light.Flags == (0xFFFF_FFF8 | typeCode),
                "Mapping a known type changed unrelated raw flag bits.");
        }

        for (uint typeCode = 5; typeCode < 8; typeCode++)
        {
            var light = new SceneLight(typeCode, 0, 0, 0, 0, 0, SceneLight.NoLightGaoKey);
            Require(light.KnownType is null && light.TypeCode == typeCode,
                "An undocumented low-bit type code received a guessed semantic name.");
        }
    }

    private static void VerifyMalformedPayloadsAreRejected()
    {
        byte[] validPayload = CreatePayload(0, 0, 1, 2, 3, 4, SceneLight.NoLightGaoKey);
        foreach (int invalidLength in new[] { 0, 27, 29 })
        {
            byte[] malformed = new byte[invalidLength];
            validPayload.AsSpan(0, Math.Min(validPayload.Length, invalidLength)).CopyTo(malformed);
            Require(!PcLightReader.TryRead(malformed, out var light, out var error)
                && light is null && error.Contains("exactly 28 bytes", StringComparison.Ordinal),
                $"Invalid body length {invalidLength} was not rejected with a clear diagnostic.");
        }

        foreach (int floatOffset in new[] { 8, 12, 16, 20 })
        {
            foreach (uint invalidBits in new[] { 0x7FC00000u, 0x7F800000u, 0xFF800000u })
            {
                byte[] malformed = (byte[])validPayload.Clone();
                BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(floatOffset), invalidBits);
                Require(!PcLightReader.TryRead(malformed, out var light, out var error)
                    && light is null && error.Contains("non-finite", StringComparison.Ordinal),
                    $"Non-finite serialized float at offset {floatOffset} was not rejected.");
            }
        }
    }

    private static byte[] CreatePayload(uint flags, uint color, float near, float far, float density, float i8, uint key)
    {
        byte[] payload = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, flags);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), color);
        WriteSingle(payload, 8, near);
        WriteSingle(payload, 12, far);
        WriteSingle(payload, 16, density);
        WriteSingle(payload, 20, i8);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(24), key);
        return payload;
    }

    private static void VerifyGraphicEnvelope()
    {
        byte[] body = CreatePayload(0x2928, 0x00abcdef, 2, 5, .1f, .2f, SceneLight.NoLightGaoKey);
        byte[] graphic = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(graphic, 2);
        body.CopyTo(graphic, 4);
        Require(PcLightReader.TryReadGraphic(graphic, out var light, out var error)
            && light?.Color == 0x00abcdef, $"Light GRO wrapper was not decoded: {error}");
        BinaryPrimitives.WriteUInt32LittleEndian(graphic, 6);
        Require(!PcLightReader.TryReadGraphic(graphic, out _, out _), "A non-light GRO type was accepted as a light.");
        Require(!PcLightReader.TryReadGraphic(body, out _, out _), "The body was mistaken for a complete GRO record.");
    }

    private static void WriteSingle(byte[] payload, int offset, float value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset), BitConverter.SingleToUInt32Bits(value));

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
