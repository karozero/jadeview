using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace JadeView;

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
