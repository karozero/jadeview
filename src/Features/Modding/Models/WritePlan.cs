using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace JadeView;

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
    public byte[]? RebuiltContainer { get; internal set; }

    public int TotalBytesToModify => RebuiltContainer?.Length ?? Patches.Sum(patch => patch.HasChanges ? JadeMatrix.SizeInBytes : 0);

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

        result.BytesToModify = RebuiltContainer?.Length ?? bytesToModify;
        return result;
    }

    /// <summary>
    /// Создает обратный план записи для гарантированного Undo / возврата к исходным bytesам.
    /// </summary>
    public WritePlan CreateInversePlan()
    {
        if (RebuiltContainer != null)
            throw new InvalidOperationException("Structural edits require the original container to restore; they have no in-place inverse patch.");
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

