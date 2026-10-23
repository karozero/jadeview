using System.Numerics;

namespace JadeView;

internal static partial class PcCollisionResourceReader
{
    // Serialized COL zone tags, not the ordinal values of PcCollisionShapeKind.
    private const byte BoxShapeType = 1;
    private const byte SphereShapeType = 2;
    private const byte CylinderShapeType = 3;
    private const byte ConeShapeType = 4;

    private static bool TryReadZone(ref Cursor cursor, PcCollisionWireDialect dialect,
        PcCollisionReadMode mode, out PcCollisionZone zone,
        out string? warning, out string error)
    {
        zone = null!;
        warning = null;
        error = "zone header is truncated";
        if (!cursor.TryReadByte(out byte flags)
            || !cursor.TryReadByte(out byte type)
            || !cursor.TryReadByte(out byte boneIndex)
            || !cursor.TryReadByte(out byte design)
            || !TryReadNameAndAi(ref cursor, mode, out byte[] nameBytes, out byte aiIndex)) return false;

        if (!TryReadShape(ref cursor, dialect, type, out PcCollisionShape shape,
                out warning, out error)) return false;
        zone = new PcCollisionZone(flags, type, boneIndex, design, nameBytes, aiIndex, shape);
        return true;
    }

    private static bool TryReadSpecificZone(ref Cursor cursor, PcCollisionWireDialect dialect,
        PcCollisionReadMode mode,
        out PcCollisionInstanceZone zone,
        out string? warning, out string error)
    {
        zone = null!;
        warning = null;
        error = "specific-zone header is truncated";
        if (!cursor.TryReadByte(out byte flags)
            || !cursor.TryReadByte(out byte type)
            || !cursor.TryReadByte(out byte boneIndex)
            || !cursor.TryReadByte(out byte design)) return false;

        byte colSetZoneIndex = 0;
        if (mode != PcCollisionReadMode.BinaryCompact
            && !cursor.TryReadByte(out colSetZoneIndex)) return false;
        if (!TryReadNameAndAi(ref cursor, mode, out byte[] nameBytes, out byte aiIndex)) return false;

        if (!TryReadShape(ref cursor, dialect, type, out PcCollisionShape shape,
                out warning, out error)) return false;
        zone = new PcCollisionInstanceZone(flags, type, boneIndex, design,
            colSetZoneIndex, nameBytes, aiIndex, shape);
        return true;
    }

    private static bool TryReadShape(ref Cursor cursor, PcCollisionWireDialect dialect, byte type,
        out PcCollisionShape shape, out string? warning, out string error)
    {
        shape = null!;
        warning = null;
        error = "unsupported shape type";
        switch (type)
        {
            case BoxShapeType:
                if (!cursor.TryReadVector(out Vector3 maximum)
                    || !cursor.TryReadVector(out Vector3 minimum))
                {
                    error = "box shape has invalid or truncated coordinates";
                    return false;
                }
                shape = new(PcCollisionShapeKind.Box, maximum, minimum);
                return true;
            case SphereShapeType:
                if (!cursor.TryReadVector(out Vector3 center))
                {
                    error = "sphere center is invalid or truncated";
                    return false;
                }
                if (!cursor.TryReadSingle(out float radius))
                {
                    error = "sphere radius is invalid or truncated";
                    return false;
                }
                if (radius < 0)
                {
                    error = "sphere radius is negative";
                    return false;
                }
                shape = new(PcCollisionShapeKind.Sphere, center, Radius: radius);
                return true;
            case CylinderShapeType:
                if (!cursor.TryReadVector(out Vector3 cylinderCenter))
                {
                    error = "cylinder center is invalid or truncated";
                    return false;
                }
                if (!cursor.TryReadSingle(out float firstCylinderValue)
                    || !cursor.TryReadSingle(out float secondCylinderValue))
                {
                    error = "cylinder dimensions are invalid or truncated";
                    return false;
                }
                if (firstCylinderValue < 0 || secondCylinderValue < 0)
                {
                    error = "cylinder radius or height is negative";
                    return false;
                }
                shape = new(PcCollisionShapeKind.Cylinder, cylinderCenter,
                    Radius: dialect == PcCollisionWireDialect.IrisCompact
                        ? secondCylinderValue : firstCylinderValue,
                    Height: dialect == PcCollisionWireDialect.IrisCompact
                        ? firstCylinderValue : secondCylinderValue);
                return true;
            case ConeShapeType when dialect == PcCollisionWireDialect.JadedPc:
                shape = new(PcCollisionShapeKind.UnsupportedNoPayload, UnsupportedType: type);
                warning = "Jaded's loader has no serialized cone payload branch; cone geometry remains unsupported.";
                return true;
            default:
                // Both source loaders' switches have no default payload reader/writer.
                // Preserve the unknown kind and disclose the unsupported shape semantics.
                shape = new(PcCollisionShapeKind.UnsupportedNoPayload, UnsupportedType: type);
                warning = $"{dialect} has no serialized payload branch for shape type {type}; shape geometry is unsupported.";
                return true;
        }
    }

    private static bool TryReadNameAndAi(ref Cursor cursor, PcCollisionReadMode mode,
        out byte[] nameBytes, out byte aiIndex)
    {
        nameBytes = [];
        aiIndex = 0;
        if (!cursor.TryReadUInt32(out uint nameLength)) return false;
        if (nameLength > int.MaxValue) return false;

        if (mode != PcCollisionReadMode.BinaryCompact)
        {
            if (!cursor.TryReadBytes((int)nameLength, out nameBytes)) return false;
            return cursor.TryReadByte(out aiIndex);
        }

        // LOA editor-data readers and Iris speed-mode readers skip these fields
        // in binary compact data; the name length remains serialized.
        return true;
    }

}
