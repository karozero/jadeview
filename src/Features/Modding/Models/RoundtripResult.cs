using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace JadeView;

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

