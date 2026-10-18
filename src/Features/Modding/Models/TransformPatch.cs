using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace JadeView;

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

