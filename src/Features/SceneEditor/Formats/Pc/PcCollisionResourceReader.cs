using System.Buffers.Binary;

namespace JadeView;

/// <summary>
/// Bounded readers for PC collision records. Callers select both the source dialect and whether
/// the archive stores editor-rich or binary-compact data; these choices are never auto-detected.
/// </summary>
internal static partial class PcCollisionResourceReader
{
    // Wire limits and defaults shared by the COL readers; see collision-cob-wire-audit.md.
    private const int MaximumZoneCount = 16;
    private const int MapHeaderSize = 4;
    private const ushort IrisEffectiveCrossable = 0x589A;

    internal static PcCollisionDecodeResult<PcCollisionMap> ReadMap(ReadOnlySpan<byte> data,
        PcCollisionWireDialect dialect, PcCollisionReadMode mode = PcCollisionReadMode.EditorRich)
    {
        if (!IsSupportedDialect(dialect) || !IsSupportedMode(mode))
            return UnsupportedVersion<PcCollisionMap>($"Unsupported collision wire dialect '{dialect}'.");

        if (data.Length == sizeof(uint))
        {
            uint cobKey = BinaryPrimitives.ReadUInt32LittleEndian(data);
            return Success(new PcCollisionMap(byte.MaxValue, 0, 0, [cobKey], true));
        }

        if (data.Length < MapHeaderSize)
            return Truncated<PcCollisionMap>("COL map header requires four bytes.");

        byte count = data[0];
        int expectedLength = MapHeaderSize + count * sizeof(uint);
        if (data.Length < expectedLength)
            return Truncated<PcCollisionMap>(
                $"COL map declares {count} COB keys but only {data.Length - MapHeaderSize} key bytes remain.");
        if (data.Length != expectedLength)
            return Invalid<PcCollisionMap>(
                $"COL map has {data.Length - expectedLength} trailing bytes outside its declared key list.");

        var cobKeys = new uint[count];
        for (int index = 0; index < cobKeys.Length; index++)
            cobKeys[index] = BinaryPrimitives.ReadUInt32LittleEndian(
                data.Slice(MapHeaderSize + index * sizeof(uint), sizeof(uint)));

        string[] warnings = dialect != PcCollisionWireDialect.JadedPc
            ? ["Iris source uses the same counted COL map wire shape; its child collision payloads use the Iris dialect."]
            : [];
        return Success(new PcCollisionMap(data[1], data[2], data[3], cobKeys, false), warnings);
    }

    internal static PcCollisionDecodeResult<PcCollisionSet> ReadSet(ReadOnlySpan<byte> data,
        PcCollisionWireDialect dialect, PcCollisionReadMode mode = PcCollisionReadMode.EditorRich)
    {
        if (!IsSupportedDialect(dialect) || !IsSupportedMode(mode))
            return UnsupportedVersion<PcCollisionSet>($"Unsupported collision wire dialect '{dialect}'.");

        var cursor = new Cursor(data);
        if (!cursor.TryReadByte(out byte zoneCount)
            || !cursor.TryReadByte(out byte flags))
            return Truncated<PcCollisionSet>("COL set header requires two bytes.");
        ushort? savedInstanceCount = null;
        if (mode != PcCollisionReadMode.BinaryCompact)
        {
            if (!cursor.TryReadUInt16(out ushort savedCount))
                return Truncated<PcCollisionSet>("COL set saved-instance count is truncated.");
            savedInstanceCount = savedCount;
        }
        if (zoneCount > MaximumZoneCount)
            return Invalid<PcCollisionSet>($"COL set declares {zoneCount} zones; source loaders support at most {MaximumZoneCount}.");
        if (!cursor.TryReadBytes(MaximumZoneCount, out byte[] aiIndexTranslation))
            return Truncated<PcCollisionSet>("COL set AI-index translation table is truncated.");

        var zones = new PcCollisionZone[zoneCount];
        var warnings = new List<string>();
        for (int index = 0; index < zones.Length; index++)
        {
            if (!TryReadZone(ref cursor, dialect, mode, out zones[index], out string? warning, out string error))
                return DecodeFailure<PcCollisionSet>(cursor, $"COL set zone {index}: {error}");
            if (warning != null) warnings.Add($"COL set zone {index}: {warning}");
        }

        if (!cursor.AtEnd)
            return Invalid<PcCollisionSet>($"COL set has {cursor.Remaining} trailing bytes after its declared zones.");

        return Success(new PcCollisionSet(flags, savedInstanceCount, aiIndexTranslation, zones), warnings.ToArray());
    }

    internal static PcCollisionDecodeResult<PcCollisionInstance> ReadInstance(ReadOnlySpan<byte> data,
        PcCollisionWireDialect dialect, PcCollisionReadMode mode = PcCollisionReadMode.EditorRich)
    {
        if (!IsSupportedDialect(dialect) || !IsSupportedMode(mode))
            return UnsupportedVersion<PcCollisionInstance>($"Unsupported collision wire dialect '{dialect}'.");
        if (data.Length < sizeof(uint))
            return Truncated<PcCollisionInstance>("COL instance requires a four-byte ColSet key.");

        uint colSetKey = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (data.Length == sizeof(uint))
            return Success(new PcCollisionInstance(colSetKey, 0, 0, 0, 0, [], [],
                ushort.MaxValue, 0, 0, 0, 0, 0, 0, 0, true));

        var cursor = new Cursor(data[sizeof(uint)..]);
        if (!cursor.TryReadByte(out byte zoneCount)
            || !cursor.TryReadByte(out byte sharedZoneCount)
            || !cursor.TryReadByte(out byte specificZoneCount)
            || !cursor.TryReadByte(out byte priority))
            return Truncated<PcCollisionInstance>("COL instance zone-count header requires four bytes after the ColSet key.");
        if (zoneCount > MaximumZoneCount || sharedZoneCount + specificZoneCount != zoneCount)
            return Invalid<PcCollisionInstance>(
                $"COL instance counts are inconsistent: total {zoneCount}, shared {sharedZoneCount}, specific {specificZoneCount}.");
        if (!cursor.TryReadBytes(zoneCount, out byte[] selectedZoneIndices))
            return Truncated<PcCollisionInstance>("COL instance selected-zone index array is truncated.");

        var specificZones = new PcCollisionInstanceZone[specificZoneCount];
        var warnings = new List<string>();
        for (int index = 0; index < specificZones.Length; index++)
        {
            if (!TryReadSpecificZone(ref cursor, dialect, mode, out specificZones[index], out string? warning, out string error))
                return DecodeFailure<PcCollisionInstance>(cursor,
                    $"COL instance specific zone {index}: {error}");
            if (warning != null) warnings.Add($"COL instance specific zone {index}: {warning}");
        }

        if (!cursor.TryReadUInt16(out ushort activationMask)
            || !cursor.TryReadUInt16(out ushort specificMask))
            return Truncated<PcCollisionInstance>("COL instance activation and specific masks are truncated.");
        ushort legacyField = 0;
        if (mode != PcCollisionReadMode.BinaryCompact
            && !cursor.TryReadUInt16(out legacyField))
            return Truncated<PcCollisionInstance>("COL instance legacy field is truncated.");
        if (!cursor.TryReadUInt16(out ushort serializedCrossable)
            || !cursor.TryReadUInt16(out ushort flags)
            || !cursor.TryReadByte(out byte firstLod)
            || !cursor.TryReadByte(out byte secondLod))
            return Truncated<PcCollisionInstance>("COL instance activation/flags/LOD tail is truncated.");

        if (!cursor.AtEnd)
            return Invalid<PcCollisionInstance>($"COL instance has {cursor.Remaining} trailing bytes after its declared fields.");

        bool isIris = dialect == PcCollisionWireDialect.IrisCompact;
        var instance = new PcCollisionInstance(colSetKey, zoneCount, sharedZoneCount,
            specificZoneCount, unchecked((sbyte)priority), selectedZoneIndices, specificZones,
            activationMask, specificMask, legacyField, serializedCrossable,
            isIris ? IrisEffectiveCrossable : serializedCrossable,
            flags, isIris ? secondLod : firstLod, isIris ? firstLod : secondLod, false);
        if (mode == PcCollisionReadMode.BinaryCompact)
            warnings.Add("Binary compact data omits editor-only strings, AI indices, zone references, saved-instance count, and legacy tail data; omitted fields use loader defaults.");
        return Success(instance, warnings.ToArray());
    }

}
