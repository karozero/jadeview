using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace JadeView;

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

