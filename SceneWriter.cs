using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace JadeView;

/// <summary>
/// Флаги типа матрицы трансформации Jade (MATH_tdst_Matrix.lType).
/// Согласованы с reference-исходниками Jaded (SDK/Sources/MATHs/MATHstruct.h).
/// </summary>
[Flags]
public enum JadeMatrixType : int
{
    Identity = 0,
    Translation = 2,      // MATH_Ci_Translation: матрица содержит перенос
    Rotation = 4,         // MATH_Ci_Rotation: матрица содержит поворот (не единичная 3x3)
    Scale = 8,            // MATH_Ci_Scale: матрица содержит масштабирование по локальным осям
    OScale = 16,          // MATH_Ci_OScale: ориентированное масштабирование
    AnyScale = 32,        // MATH_Ci_AnyScale: произвольное масштабирование
    Complex = Scale | Rotation,
    AnyFormOfScale = Scale | OScale | AnyScale,
    AnyFlag = AnyFormOfScale | Rotation | Translation
}

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

/// <summary>
/// Точечный патч матрицы трансформации отдельного объекта GAO в сыром контейнере.
/// Содержит контрольные bytesы до записи для гарантированного preflight-тестирования.
/// </summary>
public sealed class TransformPatch
{
    public string ObjectId { get; init; } = "";
    public uint GaoKey { get; init; }
    public string ObjectName { get; init; } = "";

    /// <summary>
    /// Диапазон всей записи GAO в контейнере (включая 4 bytesа длины).
    /// </summary>
    public SourceRange RecordRange { get; init; }

    /// <summary>
    /// Диапазон 68 bytes матрицы внутри контейнера.
    /// </summary>
    public SourceRange MatrixRange { get; init; }

    /// <summary>
    /// Ожидаемые исходные 68 bytes матрицы (контрольный снимок для сверки перед записью).
    /// </summary>
    public byte[] ExpectedOriginalBytes { get; init; } = Array.Empty<byte>();

    /// <summary>
    /// Новые 68 bytes матрицы для записи.
    /// </summary>
    public byte[] PatchedBytes { get; init; } = Array.Empty<byte>();

    public TransformData OldTransform { get; init; } = new();
    public TransformData NewTransform { get; init; } = new();
    public EditCapabilities RequiredCapabilities { get; init; } = EditCapabilities.AllTransform;
    public bool IsTranslationOnly { get; init; }

    public bool HasChanges => !ExpectedOriginalBytes.AsSpan().SequenceEqual(PatchedBytes);

    public TransformPatch CreateInverse() => new()
    {
        ObjectId = ObjectId,
        GaoKey = GaoKey,
        ObjectName = ObjectName,
        RecordRange = RecordRange,
        MatrixRange = MatrixRange,
        ExpectedOriginalBytes = (byte[])PatchedBytes.Clone(),
        PatchedBytes = (byte[])ExpectedOriginalBytes.Clone(),
        OldTransform = NewTransform,
        NewTransform = OldTransform,
        RequiredCapabilities = RequiredCapabilities,
        IsTranslationOnly = IsTranslationOnly
    };
}

/// <summary>
/// Результат выполнения preflight-проверки плана записи.
/// </summary>
public sealed class PreflightResult
{
    public bool IsSuccess => Errors.Count == 0;
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
    public int PatchesChecked { get; set; }
    public int BytesToModify { get; set; }

    public override string ToString()
    {
        if (IsSuccess)
            return $"Preflight OK: checked patches: {PatchesChecked}, bytes to modify: {BytesToModify}, warnings: {Warnings.Count}";
        return $"Preflight FAILED ({Errors.Count} errors): {string.Join("; ", Errors)}";
    }
}

/// <summary>
/// Результат верификации обратимости и изоляции patches (roundtrip check).
/// </summary>
public sealed class RoundtripResult
{
    public bool IsSuccess => Errors.Count == 0;
    public List<string> Errors { get; } = [];
    public int TotalBytesCompared { get; set; }
    public int TotalBytesModified { get; set; }
    public int ExpectedBytesModified { get; set; }
    public List<SourceRange> ModifiedRanges { get; } = [];

    public override string ToString()
    {
        if (IsSuccess)
            return $"Roundtrip OK: compared bytes: {TotalBytesCompared}, modified: {TotalBytesModified} (expected: {ExpectedBytesModified})";
        return $"Roundtrip FAILED ({Errors.Count} errors): {string.Join("; ", Errors)}";
    }
}

/// <summary>
/// План записи точечных изменений трансформаций objects.
/// Содержит список изолированных patches и выполняет preflight-проверки.
/// </summary>
public sealed class WritePlan
{
    public string ContainerName { get; init; } = "";
    public int ExpectedContainerSize { get; init; }
    public string ExpectedContainerSha256 { get; init; } = "";
    public List<TransformPatch> Patches { get; } = [];
    public List<string> Diagnostics { get; } = [];

    public int TotalBytesToModify => Patches.Sum(p => p.HasChanges ? JadeMatrix.SizeInBytes : 0);

    /// <summary>
    /// Добавляет точечный патч для перемещения объекта (Translation-only).
    /// All остальные поля матрицы (поворот, масштабирование, неиспользуемые флаги) сохраняются побайтово.
    /// </summary>
    public TransformPatch AddTranslationPatch(SceneObject target, Vector3 newTranslation, ReadOnlySpan<byte> containerBytes)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!target.Transform.MatrixByteRange.IsValid || target.Transform.MatrixByteRange.Size != JadeMatrix.SizeInBytes)
            throw new ArgumentException($"Object {target.Name} ({target.GaoKey:X8}) has no valid MatrixByteRange.", nameof(target));

        int offset = target.Transform.MatrixByteRange.Offset;
        if (offset < 0 || offset + JadeMatrix.SizeInBytes > containerBytes.Length)
            throw new ArgumentOutOfRangeException(nameof(containerBytes), $"Matrix offset 0x{offset:X8} outside container bounds ({containerBytes.Length} bytes).");

        var originalMatrixBytes = containerBytes.Slice(offset, JadeMatrix.SizeInBytes).ToArray();
        var currentMatrix = JadeMatrix.Read(originalMatrixBytes);

        var patchedMatrix = currentMatrix;
        patchedMatrix.SetTranslation(newTranslation);
        var patchedBytes = patchedMatrix.ToByteArray();

        var oldTransform = currentMatrix.ToTransformData(target.Transform.MatrixByteRange);
        var newTransform = patchedMatrix.ToTransformData(target.Transform.MatrixByteRange);

        var patch = new TransformPatch
        {
            ObjectId = target.Id,
            GaoKey = target.GaoKey,
            ObjectName = target.Name,
            RecordRange = target.RecordRange,
            MatrixRange = target.Transform.MatrixByteRange,
            ExpectedOriginalBytes = originalMatrixBytes,
            PatchedBytes = patchedBytes,
            OldTransform = oldTransform,
            NewTransform = newTransform,
            RequiredCapabilities = EditCapabilities.Translate,
            IsTranslationOnly = true
        };

        Patches.Add(patch);
        return patch;
    }

    /// <summary>
    /// Добавляет точечный патч полной трансформации (перемещение, поворот, масштаб).
    /// </summary>
    public TransformPatch AddTransformPatch(SceneObject target, Vector3 newTranslation, Quaternion newRotation, Vector3 newScale, ReadOnlySpan<byte> containerBytes)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!target.Transform.MatrixByteRange.IsValid || target.Transform.MatrixByteRange.Size != JadeMatrix.SizeInBytes)
            throw new ArgumentException($"Object {target.Name} ({target.GaoKey:X8}) has no valid MatrixByteRange.", nameof(target));

        int offset = target.Transform.MatrixByteRange.Offset;
        if (offset < 0 || offset + JadeMatrix.SizeInBytes > containerBytes.Length)
            throw new ArgumentOutOfRangeException(nameof(containerBytes), $"Matrix offset 0x{offset:X8} outside container bounds ({containerBytes.Length} bytes).");

        var originalMatrixBytes = containerBytes.Slice(offset, JadeMatrix.SizeInBytes).ToArray();
        var currentMatrix = JadeMatrix.Read(originalMatrixBytes);

        var patchedMatrix = currentMatrix;
        patchedMatrix.SetTransform(newTranslation, newRotation, newScale);
        var patchedBytes = patchedMatrix.ToByteArray();

        var oldTransform = currentMatrix.ToTransformData(target.Transform.MatrixByteRange);
        var newTransform = patchedMatrix.ToTransformData(target.Transform.MatrixByteRange);

        EditCapabilities required = EditCapabilities.None;
        if (currentMatrix.Translation != newTranslation) required |= EditCapabilities.Translate;
        if (currentMatrix.Rotation != newRotation) required |= EditCapabilities.Rotate;
        if (currentMatrix.Scale != newScale) required |= EditCapabilities.Scale;

        var patch = new TransformPatch
        {
            ObjectId = target.Id,
            GaoKey = target.GaoKey,
            ObjectName = target.Name,
            RecordRange = target.RecordRange,
            MatrixRange = target.Transform.MatrixByteRange,
            ExpectedOriginalBytes = originalMatrixBytes,
            PatchedBytes = patchedBytes,
            OldTransform = oldTransform,
            NewTransform = newTransform,
            RequiredCapabilities = required == EditCapabilities.None ? EditCapabilities.Translate : required,
            IsTranslationOnly = (required == EditCapabilities.Translate)
        };

        Patches.Add(patch);
        return patch;
    }

    /// <summary>
    /// Выполняет исчерпывающие preflight-проверки целостности и безопасности перед записью.
    /// </summary>
    public PreflightResult PreflightCheck(byte[] containerBytes)
    {
        var result = new PreflightResult();
        if (containerBytes == null)
        {
            result.Errors.Add("Container buffer is null.");
            return result;
        }

        if (ExpectedContainerSize > 0 && containerBytes.Length != ExpectedContainerSize)
        {
            result.Errors.Add($"Container size ({containerBytes.Length} bytes) does not match expected ({ExpectedContainerSize} bytes).");
        }

        if (!string.IsNullOrEmpty(ExpectedContainerSha256))
        {
            var actualHash = Convert.ToHexStringLower(SHA256.HashData(containerBytes));
            if (!string.Equals(actualHash, ExpectedContainerSha256, StringComparison.OrdinalIgnoreCase))
            {
                result.Errors.Add($"Container SHA-256 checksum ({actualHash}) does not match expected ({ExpectedContainerSha256}).");
            }
        }

        // Проверка отсутствия взаимных пересечений диапазонов patches
        var sortedPatches = Patches.OrderBy(p => p.MatrixRange.Offset).ToList();
        for (int i = 0; i < sortedPatches.Count - 1; i++)
        {
            var p1 = sortedPatches[i];
            var p2 = sortedPatches[i + 1];
            if (p1.MatrixRange.Offset + p1.MatrixRange.Size > p2.MatrixRange.Offset)
            {
                result.Errors.Add($"Overlapping patches: objects '{p1.ObjectName}' (0x{p1.MatrixRange.Offset:X8}) and '{p2.ObjectName}' (0x{p2.MatrixRange.Offset:X8}) overlap.");
            }
        }

        int bytesToModify = 0;

        foreach (var patch in Patches)
        {
            result.PatchesChecked++;

            // 1. Проверка диапазона матрицы
            if (!patch.MatrixRange.IsValid || patch.MatrixRange.Size != JadeMatrix.SizeInBytes)
            {
                result.Errors.Add($"Object '{patch.ObjectName}' (0x{patch.GaoKey:X8}): invalid MatrixRange ({patch.MatrixRange.Offset}, {patch.MatrixRange.Size}).");
                continue;
            }

            if (patch.MatrixRange.Offset < 0 || patch.MatrixRange.Offset + JadeMatrix.SizeInBytes > containerBytes.Length)
            {
                result.Errors.Add($"Object '{patch.ObjectName}' (0x{patch.GaoKey:X8}): MatrixRange outside container bounds.");
                continue;
            }

            // 2. Проверка сигнатуры GAO
            if (patch.RecordRange.IsValid)
            {
                int magicPos = patch.RecordRange.Offset + 4;
                if (magicPos + 4 <= containerBytes.Length)
                {
                    uint magic = BinaryPrimitives.ReadUInt32LittleEndian(containerBytes.AsSpan(magicPos, 4));
                    if (magic != SceneWriter.GaoMagic)
                    {
                        result.Errors.Add($"Object '{patch.ObjectName}' (0x{patch.GaoKey:X8}): at offset 0x{magicPos:X8} missing .gao signature (got 0x{magic:X8}).");
                    }
                }

                // Проверка выравнивания относительного смещения матрицы
                int expectedMatrixOffset = patch.RecordRange.Offset + 4 + SceneWriter.GaoMatrixRelativeOffset;
                if (patch.MatrixRange.Offset != expectedMatrixOffset)
                {
                    result.Warnings.Add($"Object '{patch.ObjectName}': MatrixRange.Offset (0x{patch.MatrixRange.Offset:X8}) differs from canonical offset (0x{expectedMatrixOffset:X8}).");
                }
            }

            // 3. Сверка контрольных bytesов перед записью
            if (patch.ExpectedOriginalBytes.Length == JadeMatrix.SizeInBytes)
            {
                var currentBytes = containerBytes.AsSpan(patch.MatrixRange.Offset, JadeMatrix.SizeInBytes);
                if (!currentBytes.SequenceEqual(patch.ExpectedOriginalBytes))
                {
                    result.Errors.Add($"Object '{patch.ObjectName}' (0x{patch.GaoKey:X8}): Original matrix bytes do not match the expected state. Data may have been overwritten.");
                }
            }
            else
            {
                result.Errors.Add($"Object '{patch.ObjectName}': original byte count ({patch.ExpectedOriginalBytes.Length}) must be 68.");
            }

            // 4. Проверка корректности новых bytes матрицы
            if (patch.PatchedBytes.Length != JadeMatrix.SizeInBytes)
            {
                result.Errors.Add($"Object '{patch.ObjectName}': write buffer size ({patch.PatchedBytes.Length}) must be 68 bytes.");
                continue;
            }

            var patchedMatrix = JadeMatrix.Read(patch.PatchedBytes);
            if (!patchedMatrix.IsValid)
            {
                result.Errors.Add($"Object '{patch.ObjectName}': new matrix contains invalid values (NaN, Infinity or invalid W={patchedMatrix.W}).");
            }

            if (patch.HasChanges)
            {
                bytesToModify += JadeMatrix.SizeInBytes;
            }
        }

        result.BytesToModify = bytesToModify;
        return result;
    }

    /// <summary>
    /// Создает обратный план записи для гарантированного Undo / возврата к исходным bytesам.
    /// </summary>
    public WritePlan CreateInversePlan()
    {
        var inverse = new WritePlan
        {
            ContainerName = ContainerName,
            ExpectedContainerSize = ExpectedContainerSize,
            ExpectedContainerSha256 = ""
        };

        foreach (var p in Patches)
        {
            inverse.Patches.Add(p.CreateInverse());
        }

        return inverse;
    }
}

/// <summary>
/// Главный исполнитель безопасной точечной сериализации сцен JadeView (Агент A3).
/// Выполняет in-place patch матриц GAO в сырых bytesах контейнеров FF0 без изменения размеров записей,
/// с preflight-проверкой контрольных bytesов и верификацией обратимости (roundtrip verification).
/// </summary>
public static class SceneWriter
{
    public const uint GaoMagic = 0x6F61672E;            // ".gao" в little-endian
    public const int GaoMatrixRelativeOffset = 0x16;     // 22 bytesа от сигнатуры .gao
    public const int MatrixByteSize = JadeMatrix.SizeInBytes; // 68 bytes

    /// <summary>
    /// Запуск preflight-проверки плана записи над контейнером.
    /// Не модифицирует bytesы контейнера.
    /// </summary>
    public static PreflightResult Preflight(byte[] containerBytes, WritePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.PreflightCheck(containerBytes);
    }

    /// <summary>
    /// Применяет точечные патчи in-place к переданному массиву bytes контейнера.
    /// Перед записью обязательно выполняет preflight-проверку; в случае errors выбрасывает исключение.
    /// </summary>
    public static PreflightResult Apply(byte[] containerBytes, WritePlan plan)
    {
        ArgumentNullException.ThrowIfNull(containerBytes);
        ArgumentNullException.ThrowIfNull(plan);

        var preflight = plan.PreflightCheck(containerBytes);
        if (!preflight.IsSuccess)
        {
            throw new InvalidOperationException($"Preflight rejected write plan: {string.Join("; ", preflight.Errors)}");
        }

        foreach (var patch in plan.Patches)
        {
            if (!patch.HasChanges) continue;
            patch.PatchedBytes.AsSpan().CopyTo(containerBytes.AsSpan(patch.MatrixRange.Offset, MatrixByteSize));
        }

        return preflight;
    }

    /// <summary>
    /// Чистая функциональная операция: создает модифицированную копию контейнера с применением плана.
    /// Исходный массив оригинальных bytes гарантированно не изменяется.
    /// Результат проходит встроенную верификацию изоляции roundtrip.
    /// </summary>
    public static byte[] CreatePatchedContainer(byte[] originalContainerBytes, WritePlan plan)
    {
        ArgumentNullException.ThrowIfNull(originalContainerBytes);
        ArgumentNullException.ThrowIfNull(plan);

        var patchedBytes = (byte[])originalContainerBytes.Clone();
        Apply(patchedBytes, plan);

        var roundtrip = VerifyRoundtrip(originalContainerBytes, patchedBytes, plan);
        if (!roundtrip.IsSuccess)
        {
            throw new InvalidOperationException($"Roundtrip verification failed: {string.Join("; ", roundtrip.Errors)}");
        }

        return patchedBytes;
    }

    /// <summary>
    /// Проверяет изоляцию и корректность patches (Roundtrip Verification).
    /// Гарантирует, что:
    ///   1. Ни единый bytes за пределами целевых блоков матриц не был изменен.
    ///   2. Целевые блоки матриц содержат в точности новые значения.
    ///   3. При отсутствии изменений массивы побайтово идентичны.
    ///   4. Применение обратного патча возвращает 100% SHA-256 идентичность исходнику.
    /// </summary>
    public static RoundtripResult VerifyRoundtrip(byte[] originalBytes, byte[] patchedBytes, WritePlan plan)
    {
        ArgumentNullException.ThrowIfNull(originalBytes);
        ArgumentNullException.ThrowIfNull(patchedBytes);
        ArgumentNullException.ThrowIfNull(plan);

        var result = new RoundtripResult
        {
            TotalBytesCompared = Math.Min(originalBytes.Length, patchedBytes.Length),
            ExpectedBytesModified = plan.TotalBytesToModify
        };

        if (originalBytes.Length != patchedBytes.Length)
        {
            result.Errors.Add($"Container length changed: before {originalBytes.Length}, after {patchedBytes.Length}. In-place patches must preserve size.");
            return result;
        }

        // Построение маски разрешенных к изменению диапазонов bytesов
        var allowedRanges = plan.Patches
            .Where(p => p.HasChanges)
            .Select(p => p.MatrixRange)
            .ToList();

        int totalModified = 0;
        int diffStart = -1;

        for (int i = 0; i < originalBytes.Length; i++)
        {
            if (originalBytes[i] != patchedBytes[i])
            {
                totalModified++;
                if (diffStart == -1) diffStart = i;

                // Проверяем, входит ли измененный bytes хотя бы в один разрешенный диапазон матрицы
                bool isAllowed = false;
                foreach (var r in allowedRanges)
                {
                    if (i >= r.Offset && i < r.Offset + r.Size)
                    {
                        isAllowed = true;
                        break;
                    }
                }

                if (!isAllowed)
                {
                    result.Errors.Add($"Unauthorized byte change at offset 0x{i:X8}! Byte is outside permitted matrix ranges.");
                    break;
                }
            }
            else
            {
                if (diffStart != -1)
                {
                    result.ModifiedRanges.Add(new SourceRange(diffStart, i - diffStart));
                    diffStart = -1;
                }
            }
        }

        if (diffStart != -1)
        {
            result.ModifiedRanges.Add(new SourceRange(diffStart, originalBytes.Length - diffStart));
        }

        result.TotalBytesModified = totalModified;

        // Если изменений не планировалось, массивы обязаны быть идентичны
        if (plan.Patches.Count == 0 || plan.TotalBytesToModify == 0)
        {
            if (totalModified != 0)
            {
                result.Errors.Add($"Expected 0 modified bytes, found {totalModified} differences.");
            }
            return result;
        }

        // Проверка значений в пропатченном буфере для каждого патча
        foreach (var patch in plan.Patches)
        {
            if (!patch.HasChanges) continue;

            var writtenBytes = patchedBytes.AsSpan(patch.MatrixRange.Offset, MatrixByteSize);
            if (!writtenBytes.SequenceEqual(patch.PatchedBytes))
            {
                result.Errors.Add($"Object '{patch.ObjectName}': bytes in container at offset 0x{patch.MatrixRange.Offset:X8} do not match PatchedBytes.");
            }

            var readMatrix = JadeMatrix.Read(writtenBytes);
            var expectedMatrix = JadeMatrix.Read(patch.PatchedBytes);
            if (!readMatrix.Equals(expectedMatrix))
            {
                result.Errors.Add($"Object '{patch.ObjectName}': parsed matrix does not match the patch.");
            }
        }

        // Проверка обратимости (Undo / Reversibility test)
        var inversePlan = plan.CreateInversePlan();
        var revertedBytes = (byte[])patchedBytes.Clone();
        var revertPreflight = inversePlan.PreflightCheck(revertedBytes);
        if (!revertPreflight.IsSuccess)
        {
            result.Errors.Add($"Inverse patch preflight failed: {string.Join("; ", revertPreflight.Errors)}");
        }
        else
        {
            foreach (var p in inversePlan.Patches)
            {
                if (!p.HasChanges) continue;
                p.PatchedBytes.AsSpan().CopyTo(revertedBytes.AsSpan(p.MatrixRange.Offset, MatrixByteSize));
            }

            if (!revertedBytes.AsSpan().SequenceEqual(originalBytes))
            {
                result.Errors.Add("Inverse write plan did not restore original bytes.");
            }
        }

        return result;
    }

    /// <summary>
    /// Сохраняет измененный контейнер в новый BF-архив через staging-механизм Archive.Save.
    /// Исходный игровой BF открывается только на чтение; запись производится во временный файл
    /// с последующим атомарным переносом в целевой файл назначения.
    /// </summary>
    public static void BuildAndSaveArchive(
        Archive baseArchive,
        string destinationBfPath,
        int containerEntryIndex,
        byte[] patchedContainerBytes,
        Action<float>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(baseArchive);
        ArgumentNullException.ThrowIfNull(patchedContainerBytes);
        if (string.IsNullOrWhiteSpace(destinationBfPath))
            throw new ArgumentException("Target archive path must not be empty.", nameof(destinationBfPath));

        if (containerEntryIndex < 0 || containerEntryIndex >= baseArchive.Entries.Count)
            throw new ArgumentOutOfRangeException(nameof(containerEntryIndex), "Container entry index outside archive bounds.");

        string tempStagingFile = Path.Combine(Path.GetTempPath(), $"jade_staging_{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(tempStagingFile, patchedContainerBytes);
            var replacements = new Dictionary<int, string>
            {
                [containerEntryIndex] = tempStagingFile
            };

            baseArchive.Save(destinationBfPath, replacements, progress);
        }
        finally
        {
            if (File.Exists(tempStagingFile))
            {
                try { File.Delete(tempStagingFile); } catch { /* ignore temp cleanup errors */ }
            }
        }
    }

    /// <summary>
    /// Валидирует запись GAO по ее смещению в сырых bytesах контейнера.
    /// Возвращает true и вычисленный MatrixRange, если сигнатура и структура корректны.
    /// </summary>
    public static bool ValidateGaoRecord(
        ReadOnlySpan<byte> containerBytes,
        SourceRange recordRange,
        out SourceRange matrixRange,
        out string? error)
    {
        matrixRange = default;
        error = null;

        if (!recordRange.IsValid || recordRange.Offset < 0 || recordRange.Offset + 4 > containerBytes.Length)
        {
            error = "RecordRange outside container bounds.";
            return false;
        }

        int len = BinaryPrimitives.ReadInt32LittleEndian(containerBytes.Slice(recordRange.Offset, 4));
        if (len < 0x20 || recordRange.Offset + 4 + len > containerBytes.Length)
        {
            error = $"Invalid GAO record length ({len} bytes).";
            return false;
        }

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(containerBytes.Slice(recordRange.Offset + 4, 4));
        if (magic != GaoMagic)
        {
            error = $"Record signature at offset 0x{recordRange.Offset + 4:X8} is not .gao (0x{magic:X8}).";
            return false;
        }

        int matOffset = recordRange.Offset + 4 + GaoMatrixRelativeOffset;
        if (matOffset + MatrixByteSize > recordRange.Offset + 4 + len)
        {
            error = "Matrix outside GAO record bounds.";
            return false;
        }

        matrixRange = new SourceRange(matOffset, MatrixByteSize);
        return true;
    }
}
