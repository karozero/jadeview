using System.Numerics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JadeView;

/// <summary>
/// Сводный манифест проекта модификации для предпросмотра, журналирования и экспорта.
/// </summary>
public sealed class ModManifest
{
    public string ModName { get; init; } = "";
    public int FormatVersion { get; init; }
    public string BaseBfSha256 { get; init; } = "";
    public int TotalModifiedContainers { get; init; }
    public int TotalModifiedObjects { get; init; }
    public List<string> AffectedContainers { get; init; } = [];
    public List<string> ObjectSummaries { get; init; } = [];
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;

    public string ToMarkdown()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Mod manifest: {ModName}");
        sb.AppendLine($"- **Format version:** {FormatVersion}");
        sb.AppendLine($"- **Base BF SHA-256:** `{BaseBfSha256}`");
        sb.AppendLine($"- **Generated:** {GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"- **Affected containers:** {TotalModifiedContainers}");
        sb.AppendLine($"- **Modified objects:** {TotalModifiedObjects}");
        sb.AppendLine();
        sb.AppendLine("## Affected containers:");
        foreach (var c in AffectedContainers)
        {
            sb.AppendLine($"- `{c}`");
        }
        sb.AppendLine();
        sb.AppendLine("## Modified objects:");
        foreach (var s in ObjectSummaries)
        {
            sb.AppendLine($"- {s}");
        }
        return sb.ToString();
    }
}

