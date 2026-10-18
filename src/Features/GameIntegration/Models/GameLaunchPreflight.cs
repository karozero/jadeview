using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace JadeView;

/// <summary>
/// Сводный отчет о путях, бэкапах и статусе перед первым применением мода и запуском игры.
/// Требование раздела 6 EDITOR_PLAN.md: «Перед первым применением показать пути».
/// </summary>
public sealed class GameLaunchPreflight
{
    public string GameDirectory { get; init; } = "";
    public string ExePath { get; init; } = "";
    public string BfPath { get; init; } = "";
    public string BackupPath { get; init; } = "";
    public bool IsExePresent { get; init; }
    public bool IsBfPresent { get; init; }
    public bool IsBackupPresent { get; init; }
    public bool IsModInstalled { get; init; }
    public bool IsGameRunning { get; init; }
    public int? RunningPid { get; init; }
    public string? ActiveModName { get; init; }
    public List<string> Warnings { get; } = [];
    public List<string> Errors { get; } = [];

    public bool CanInstallMod => IsExePresent && IsBfPresent && !IsGameRunning && Errors.Count == 0;
    public bool CanLaunch => IsExePresent && !IsGameRunning && Errors.Count == 0;

    public string ToMarkdown()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Installation preflight Beyond Good & Evil");
        sb.AppendLine($"- **Game folder:** `{GameDirectory}`");
        sb.AppendLine($"- **Executable (BGE.exe):** `{(IsExePresent ? ExePath : "NOT FOUND")}`");
        sb.AppendLine($"- **Game archive (BF):** `{(IsBfPresent ? BfPath : "NOT FOUND")}`");
        sb.AppendLine($"- **Backup:** `{(IsBackupPresent ? BackupPath : "NOT CREATED (will be created automatically)")}`");
        sb.AppendLine($"- **Test mod installed:** {(IsModInstalled ? $"Да ({ActiveModName ?? "active"})" : "Нет (оригинальный BF)")}");
        sb.AppendLine($"- **Game process status:** {(IsGameRunning ? $"ЗАПУЩЕНА (PID: {RunningPid})" : "Не запущена (безопасно)")}");

        if (Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Warnings:");
            foreach (var w in Warnings) sb.AppendLine($"- {w}");
        }

        if (Errors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Blocking errors:");
            foreach (var e in Errors) sb.AppendLine($"- {e}");
        }

        return sb.ToString();
    }
}

