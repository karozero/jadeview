using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace JadeView;

/// <summary>
/// Результат восстановления оригинального архива из резервной копии.
/// </summary>
public sealed class RestoreResult
{
    public bool IsSuccess => Error == null;
    public string RestoredPath { get; init; } = "";
    public long RestoredSize { get; init; }
    public string? Error { get; init; }

    public static RestoreResult Success(string path, long size) => new()
    {
        RestoredPath = path,
        RestoredSize = size
    };

    public static RestoreResult Failed(string error) => new()
    {
        Error = error
    };

    public override string ToString() =>
        IsSuccess
            ? $"Original archive restored: {RestoredPath} ({RestoredSize:N0} bytes)"
            : $"Restore failed: {Error}";
}

