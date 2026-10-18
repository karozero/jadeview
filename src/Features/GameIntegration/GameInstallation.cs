using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace JadeView;

/// <summary>
/// Информация об установленной игре Beyond Good &amp; Evil и путях к её файлам.
/// </summary>
public sealed class GameInstallation
{
    public const string DefaultGameDirectory = @"C:\GAME\Beyond Good & Evil";
    public const string ExeFileName = "BGE.exe";
    public const string CleanBfFileName = "sally_clean.bf";
    public const string ActiveBfFileName = "sally_clean.bf";
    public const string BackupDirName = "JadeView_Backup";
    public const string BackupFileName = "sally_clean.bf.orig_bak";

    public string GameDirectory { get; init; } = DefaultGameDirectory;
    public string ExePath { get; init; } = "";
    public string BfPath { get; init; } = "";
    public string BackupPath { get; init; } = "";

    public bool ExeExists => !string.IsNullOrEmpty(ExePath) && File.Exists(ExePath);
    public bool BfExists => !string.IsNullOrEmpty(BfPath) && File.Exists(BfPath);
    public bool BackupExists => !string.IsNullOrEmpty(BackupPath) && File.Exists(BackupPath);

    public long BfSize => BfExists ? new FileInfo(BfPath).Length : 0;
    public long BackupSize => BackupExists ? new FileInfo(BackupPath).Length : 0;

    public bool IsValid => ExeExists && BfExists;

    public List<string> ValidationErrors { get; } = [];

    public override string ToString() =>
        $"GameDir: {GameDirectory}, Exe: {(ExeExists ? "OK" : "MISSING")}, BF: {(BfExists ? "OK" : "MISSING")}, Backup: {(BackupExists ? "PRESENT" : "NO")}";

    /// <summary>
    /// Автоматическое обнаружение путей установки игры Beyond Good &amp; Evil.
    /// Проверяет переданный путь, стандартную директорию C:\GAME, реестр Windows и Program Files.
    /// </summary>
    public static GameInstallation Discover(string? customPath = null)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(customPath))
        {
            candidates.Add(customPath);
        }

        candidates.Add(DefaultGameDirectory);

        // Проверяем реестр Windows (32-bit и 64-bit ветки)
        try
        {
            string[] registryPaths = [
                @"SOFTWARE\Ubisoft\Beyond Good & Evil",
                @"SOFTWARE\WOW6432Node\Ubisoft\Beyond Good & Evil"
            ];

            foreach (var regKey in registryPaths)
            {
                using var key = Registry.LocalMachine.OpenSubKey(regKey);
                if (key != null)
                {
                    var dir = key.GetValue("Directory") as string ?? key.GetValue("InstallDir") as string;
                    if (!string.IsNullOrEmpty(dir))
                    {
                        candidates.Add(dir);
                    }
                }
            }
        }
        catch
        {
            // Игнорируем ошибки доступа к реестру
        }

        // Стандартные пути Program Files
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(pf86))
            candidates.Add(Path.Combine(pf86, "Ubisoft", "Beyond Good & Evil"));
        if (!string.IsNullOrEmpty(pf))
            candidates.Add(Path.Combine(pf, "Ubisoft", "Beyond Good & Evil"));

        // Ищем первую директорию, содержащую BGE.exe или sally_clean.bf
        string selectedDir = DefaultGameDirectory;
        foreach (var dir in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(dir))
            {
                string testExe = Path.Combine(dir, ExeFileName);
                string testBf = Path.Combine(dir, CleanBfFileName);
                if (File.Exists(testExe) || File.Exists(testBf))
                {
                    selectedDir = Path.GetFullPath(dir);
                    break;
                }
            }
        }

        string exePath = Path.Combine(selectedDir, ExeFileName);
        string bfPath = Path.Combine(selectedDir, ActiveBfFileName);

        // Путь резервной копии: сначала проверяем отдельную подпапку JadeView_Backup, затем .orig_bak в корне
        string backupDir = Path.Combine(selectedDir, BackupDirName);
        string subfolderBackup = Path.Combine(backupDir, CleanBfFileName);
        string rootBackup = Path.Combine(selectedDir, BackupFileName);

        string backupPath = File.Exists(subfolderBackup) ? subfolderBackup : rootBackup;

        var install = new GameInstallation
        {
            GameDirectory = selectedDir,
            ExePath = exePath,
            BfPath = bfPath,
            BackupPath = backupPath
        };

        if (!install.ExeExists)
            install.ValidationErrors.Add($"Game executable {ExeFileName} not found at: {exePath}");

        if (!install.BfExists)
            install.ValidationErrors.Add($"Main game archive {ActiveBfFileName} not found at: {bfPath}");

        return install;
    }
}

