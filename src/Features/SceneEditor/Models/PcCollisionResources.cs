using System.Numerics;

namespace JadeView;

internal enum PcCollisionWireDialect
{
    JadedPc,
    IrisCompact,
}

internal enum PcCollisionReadMode
{
    EditorRich,
    BinaryCompact
}

internal enum PcCollisionDecodeStatus
{
    Success,
    Truncated,
    InvalidData,
    UnsupportedVersion,
    UnsupportedFeature
}

internal sealed record PcCollisionDecodeResult<T>(PcCollisionDecodeStatus Status, T? Value,
    string Diagnostic, string[] Warnings) where T : class
{
    internal bool Success => Status == PcCollisionDecodeStatus.Success;
}

internal sealed record PcCollisionMap(byte Activation, byte CustomBits1, byte CustomBits2,
    uint[] CobKeys, bool IsSingleCobShorthand);

internal sealed record PcCollisionInstance(uint ColSetKey, byte ZoneCount, byte SharedZoneCount,
    byte SpecificZoneCount, sbyte Priority, byte[] SelectedZoneIndices,
    PcCollisionInstanceZone[] SpecificZones, ushort ActivationMask, ushort SpecificMask,
    ushort LegacyField, ushort SerializedCrossableField, ushort EffectiveCrossable,
    ushort Flags, byte MaxLod, byte MinLod,
    bool IsColSetOnlyShorthand);

internal sealed record PcCollisionSet(byte Flags, ushort? SavedInstanceCount,
    byte[] AiIndexTranslation, PcCollisionZone[] Zones);

internal sealed record PcCollisionZone(byte Flags, byte Type, byte BoneIndex, byte Design,
    byte[] NameBytes, byte AiIndex, PcCollisionShape Shape);

internal sealed record PcCollisionInstanceZone(byte Flags, byte Type, byte BoneIndex, byte Design,
    byte ColSetZoneIndex, byte[] NameBytes, byte AiIndex, PcCollisionShape Shape);

internal enum PcCollisionShapeKind
{
    Box,
    Sphere,
    Cylinder,
    UnsupportedNoPayload
}

internal sealed record PcCollisionShape(PcCollisionShapeKind Kind,
    Vector3? FirstVector = null, Vector3? SecondVector = null,
    float? Radius = null, float? Height = null, byte? UnsupportedType = null);
