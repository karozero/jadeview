using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace JadeView;

/// <summary>
/// Представление 68-байтовой матрицы трансформации Jade (MATH_tdst_Matrix).
/// Структура памяти:
///   Offset  0..15: Ix, Iy, Iz, Sx (Строка 0: ось X и масштаб X)
///   Offset 16..31: Jx, Jy, Jz, Sy (Строка 1: ось Y и масштаб Y)
///   Offset 32..47: Kx, Ky, Kz, Sz (Строка 2: ось Z и масштаб Z)
///   Offset 48..63: Tx, Ty, Tz, W  (Строка 3: вектор переноса T и однородная координата W == 1.0f)
///   Offset 64..67: lType (32-битные флаги типа матрицы)
/// </summary>
public struct JadeMatrix : IEquatable<JadeMatrix>
{
    public const int SizeInBytes = 68;

    public float Ix;
    public float Iy;
    public float Iz;
    public float Sx;

    public float Jx;
    public float Jy;
    public float Jz;
    public float Sy;

    public float Kx;
    public float Ky;
    public float Kz;
    public float Sz;

    public float Tx;
    public float Ty;
    public float Tz;
    public float W;

    public int Type;

    public static JadeMatrix Identity => new()
    {
        Ix = 1f, Iy = 0f, Iz = 0f, Sx = 0f,
        Jx = 0f, Jy = 1f, Jz = 0f, Sy = 0f,
        Kx = 0f, Ky = 0f, Kz = 1f, Sz = 0f,
        Tx = 0f, Ty = 0f, Tz = 0f, W = 1f,
        Type = (int)JadeMatrixType.Identity
    };

    public Vector3 Translation
    {
        readonly get => new(Tx, Ty, Tz);
        set
        {
            Tx = value.X;
            Ty = value.Y;
            Tz = value.Z;
            UpdateTranslationFlag();
        }
    }

    public Vector3 Scale
    {
        readonly get
        {
            if ((Type & (int)JadeMatrixType.Scale) != 0 || (Sx != 0f || Sy != 0f || Sz != 0f))
                return new(Sx, Sy, Sz);
            return Vector3.One;
        }
        set
        {
            Sx = value.X;
            Sy = value.Y;
            Sz = value.Z;
            UpdateScaleFlag();
        }
    }

    public Quaternion Rotation
    {
        readonly get
        {
            var rotMat = new Matrix4x4(
                Ix, Iy, Iz, 0f,
                Jx, Jy, Jz, 0f,
                Kx, Ky, Kz, 0f,
                0f, 0f, 0f, 1f
            );
            return Quaternion.CreateFromRotationMatrix(rotMat);
        }
        set
        {
            var rotMat = Matrix4x4.CreateFromQuaternion(value);
            Ix = rotMat.M11; Iy = rotMat.M12; Iz = rotMat.M13;
            Jx = rotMat.M21; Jy = rotMat.M22; Jz = rotMat.M23;
            Kx = rotMat.M31; Ky = rotMat.M32; Kz = rotMat.M33;
            UpdateRotationFlag(value);
        }
    }

    public readonly Matrix4x4 RawMatrix => new(
        Ix, Iy, Iz, Sx,
        Jx, Jy, Jz, Sy,
        Kx, Ky, Kz, Sz,
        Tx, Ty, Tz, W
    );

    public readonly bool IsValid =>
        !float.IsNaN(Ix) && !float.IsInfinity(Ix) &&
        !float.IsNaN(Iy) && !float.IsInfinity(Iy) &&
        !float.IsNaN(Iz) && !float.IsInfinity(Iz) &&
        !float.IsNaN(Jx) && !float.IsInfinity(Jx) &&
        !float.IsNaN(Jy) && !float.IsInfinity(Jy) &&
        !float.IsNaN(Jz) && !float.IsInfinity(Jz) &&
        !float.IsNaN(Kx) && !float.IsInfinity(Kx) &&
        !float.IsNaN(Ky) && !float.IsInfinity(Ky) &&
        !float.IsNaN(Kz) && !float.IsInfinity(Kz) &&
        !float.IsNaN(Tx) && !float.IsInfinity(Tx) &&
        !float.IsNaN(Ty) && !float.IsInfinity(Ty) &&
        !float.IsNaN(Tz) && !float.IsInfinity(Tz) &&
        !float.IsNaN(Sx) && !float.IsInfinity(Sx) &&
        !float.IsNaN(Sy) && !float.IsInfinity(Sy) &&
        !float.IsNaN(Sz) && !float.IsInfinity(Sz) &&
        !float.IsNaN(W)  && !float.IsInfinity(W) &&
        Math.Abs(W - 1.0f) < 0.05f;

    public void SetTranslation(Vector3 translation)
    {
        Tx = translation.X;
        Ty = translation.Y;
        Tz = translation.Z;
        UpdateTranslationFlag();
    }

    public void SetRotation(Quaternion rotation)
    {
        Rotation = rotation;
    }

    public void SetScale(Vector3 scale)
    {
        Scale = scale;
    }

    public void SetTransform(Vector3 translation, Quaternion rotation, Vector3 scale)
    {
        SetRotation(rotation);
        SetScale(scale);
        SetTranslation(translation);
    }

    private void UpdateTranslationFlag()
    {
        if (Math.Abs(Tx) > 1e-4f || Math.Abs(Ty) > 1e-4f || Math.Abs(Tz) > 1e-4f)
            Type |= (int)JadeMatrixType.Translation;
        else
            Type &= ~(int)JadeMatrixType.Translation;
    }

    private void UpdateRotationFlag(Quaternion q)
    {
        if (Math.Abs(q.W - 1.0f) > 1e-4f || Math.Abs(q.X) > 1e-4f || Math.Abs(q.Y) > 1e-4f || Math.Abs(q.Z) > 1e-4f)
            Type |= (int)JadeMatrixType.Rotation;
        else
            Type &= ~(int)JadeMatrixType.Rotation;
    }

    private void UpdateScaleFlag()
    {
        if (Math.Abs(Sx - 1.0f) > 1e-4f || Math.Abs(Sy - 1.0f) > 1e-4f || Math.Abs(Sz - 1.0f) > 1e-4f)
            Type |= (int)JadeMatrixType.Scale;
        else
            Type &= ~(int)JadeMatrixType.Scale;
    }

    public static JadeMatrix Read(ReadOnlySpan<byte> span)
    {
        if (span.Length < SizeInBytes)
            throw new ArgumentException($"Slice length ({span.Length}) is smaller than Jade matrix size ({SizeInBytes}).", nameof(span));

        return new JadeMatrix
        {
            Ix = BinaryPrimitives.ReadSingleLittleEndian(span[0..4]),
            Iy = BinaryPrimitives.ReadSingleLittleEndian(span[4..8]),
            Iz = BinaryPrimitives.ReadSingleLittleEndian(span[8..12]),
            Sx = BinaryPrimitives.ReadSingleLittleEndian(span[12..16]),

            Jx = BinaryPrimitives.ReadSingleLittleEndian(span[16..20]),
            Jy = BinaryPrimitives.ReadSingleLittleEndian(span[20..24]),
            Jz = BinaryPrimitives.ReadSingleLittleEndian(span[24..28]),
            Sy = BinaryPrimitives.ReadSingleLittleEndian(span[28..32]),

            Kx = BinaryPrimitives.ReadSingleLittleEndian(span[32..36]),
            Ky = BinaryPrimitives.ReadSingleLittleEndian(span[36..40]),
            Kz = BinaryPrimitives.ReadSingleLittleEndian(span[40..44]),
            Sz = BinaryPrimitives.ReadSingleLittleEndian(span[44..48]),

            Tx = BinaryPrimitives.ReadSingleLittleEndian(span[48..52]),
            Ty = BinaryPrimitives.ReadSingleLittleEndian(span[52..56]),
            Tz = BinaryPrimitives.ReadSingleLittleEndian(span[56..60]),
            W  = BinaryPrimitives.ReadSingleLittleEndian(span[60..64]),

            Type = BinaryPrimitives.ReadInt32LittleEndian(span[64..68])
        };
    }

    public readonly void Write(Span<byte> span)
    {
        if (span.Length < SizeInBytes)
            throw new ArgumentException($"Destination buffer length ({span.Length}) is smaller than Jade matrix size ({SizeInBytes}).", nameof(span));

        BinaryPrimitives.WriteSingleLittleEndian(span[0..4], Ix);
        BinaryPrimitives.WriteSingleLittleEndian(span[4..8], Iy);
        BinaryPrimitives.WriteSingleLittleEndian(span[8..12], Iz);
        BinaryPrimitives.WriteSingleLittleEndian(span[12..16], Sx);

        BinaryPrimitives.WriteSingleLittleEndian(span[16..20], Jx);
        BinaryPrimitives.WriteSingleLittleEndian(span[20..24], Jy);
        BinaryPrimitives.WriteSingleLittleEndian(span[24..28], Jz);
        BinaryPrimitives.WriteSingleLittleEndian(span[28..32], Sy);

        BinaryPrimitives.WriteSingleLittleEndian(span[32..36], Kx);
        BinaryPrimitives.WriteSingleLittleEndian(span[36..40], Ky);
        BinaryPrimitives.WriteSingleLittleEndian(span[40..44], Kz);
        BinaryPrimitives.WriteSingleLittleEndian(span[44..48], Sz);

        BinaryPrimitives.WriteSingleLittleEndian(span[48..52], Tx);
        BinaryPrimitives.WriteSingleLittleEndian(span[52..56], Ty);
        BinaryPrimitives.WriteSingleLittleEndian(span[56..60], Tz);
        BinaryPrimitives.WriteSingleLittleEndian(span[60..64], W);

        BinaryPrimitives.WriteInt32LittleEndian(span[64..68], Type);
    }

    public readonly byte[] ToByteArray()
    {
        var bytes = new byte[SizeInBytes];
        Write(bytes);
        return bytes;
    }

    public readonly TransformData ToTransformData(SourceRange matrixRange = default) => new()
    {
        RawMatrix = RawMatrix,
        Translation = Translation,
        Rotation = Rotation,
        Scale = Scale,
        MatrixByteRange = matrixRange
    };

    public static JadeMatrix FromTransformData(TransformData data, int originalType = 0)
    {
        var m = Identity;
        m.Type = originalType;
        m.SetTransform(data.Translation, data.Rotation, data.Scale);
        return m;
    }

    public readonly bool Equals(JadeMatrix other) =>
        Ix.Equals(other.Ix) && Iy.Equals(other.Iy) && Iz.Equals(other.Iz) && Sx.Equals(other.Sx) &&
        Jx.Equals(other.Jx) && Jy.Equals(other.Jy) && Jz.Equals(other.Jz) && Sy.Equals(other.Sy) &&
        Kx.Equals(other.Kx) && Ky.Equals(other.Ky) && Kz.Equals(other.Kz) && Sz.Equals(other.Sz) &&
        Tx.Equals(other.Tx) && Ty.Equals(other.Ty) && Tz.Equals(other.Tz) && W.Equals(other.W) &&
        Type == other.Type;

    public override readonly bool Equals(object? obj) => obj is JadeMatrix other && Equals(other);

    public override readonly int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Ix); hash.Add(Iy); hash.Add(Iz); hash.Add(Sx);
        hash.Add(Jx); hash.Add(Jy); hash.Add(Jz); hash.Add(Sy);
        hash.Add(Kx); hash.Add(Ky); hash.Add(Kz); hash.Add(Sz);
        hash.Add(Tx); hash.Add(Ty); hash.Add(Tz); hash.Add(W);
        hash.Add(Type);
        return hash.ToHashCode();
    }

    public static bool operator ==(JadeMatrix left, JadeMatrix right) => left.Equals(right);
    public static bool operator !=(JadeMatrix left, JadeMatrix right) => !left.Equals(right);

    public override readonly string ToString() =>
        $"T: ({Tx:F2}, {Ty:F2}, {Tz:F2}), S: ({Scale.X:F2}, {Scale.Y:F2}, {Scale.Z:F2}), Type: 0x{Type:X8}";
}

