using System.Numerics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JadeView;

/// <summary>
/// Результат применения проекта модификаций к SceneDocument.
/// </summary>
public sealed class ModApplyResult
{
    public bool IsSuccess => Errors.Count == 0;
    public int ObjectsUpdated { get; set; }
    public int ObjectsNotFound { get; set; }
    public List<string> UpdatedObjectNames { get; } = [];
    public List<string> MissingObjectIds { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Errors { get; } = [];

    public override string ToString() =>
        IsSuccess
            ? $"Mod applied: updated {ObjectsUpdated} objects, not found: {ObjectsNotFound}, warnings: {Warnings.Count}"
            : $"Mod application failed ({Errors.Count} errors): {string.Join("; ", Errors)}";
}

