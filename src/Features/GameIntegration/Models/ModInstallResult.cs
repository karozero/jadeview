using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace JadeView;

/// <summary>
/// Результат сборки и установки мода в игру.
/// </summary>
public sealed class ModInstallResult
{
    public bool IsSuccess => Error == null;
    public string InstalledBfPath { get; init; } = "";
    public string StagingPath { get; init; } = "";
    public int PatchesApplied { get; init; }
    public int BytesModified { get; init; }
    public PreflightResult? Preflight { get; init; }
    public string? Error { get; init; }

    public static ModInstallResult Success(string bfPath, string stagingPath, int patches, int bytes, PreflightResult preflight) => new()
    {
        InstalledBfPath = bfPath,
        StagingPath = stagingPath,
        PatchesApplied = patches,
        BytesModified = bytes,
        Preflight = preflight
    };

    public static ModInstallResult Failed(string error, PreflightResult? preflight = null) => new()
    {
        Error = error,
        Preflight = preflight
    };

    public override string ToString() =>
        IsSuccess
            ? $"Mod installed at {InstalledBfPath}: applied {PatchesApplied} patches ({BytesModified} bytes)"
            : $"Mod installation failed: {Error}";
}

