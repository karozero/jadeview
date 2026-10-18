using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace JadeView;

/// <summary>
/// Результат создания или проверки резервной копии игрового архива.
/// </summary>
public sealed class BackupResult
{
    public bool IsSuccess => Error == null;
    public string BackupPath { get; init; } = "";
    public long BackupSize { get; init; }
    public string? BackupSha256 { get; init; }
    public bool WasAlreadyPresent { get; init; }
    public string? Error { get; init; }

    public static BackupResult Success(string path, long size, string? sha256, bool wasPresent) => new()
    {
        BackupPath = path,
        BackupSize = size,
        BackupSha256 = sha256,
        WasAlreadyPresent = wasPresent
    };

    public static BackupResult Failed(string error) => new()
    {
        Error = error
    };

    public override string ToString() =>
        IsSuccess
            ? (WasAlreadyPresent
                ? $"Backup already exists: {BackupPath} ({BackupSize:N0} bytes)"
                : $"Backup created: {BackupPath} ({BackupSize:N0} bytes)")
            : $"Backup failed: {Error}";
}

