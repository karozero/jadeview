using System.Diagnostics;
using System.Security.Cryptography;

namespace JadeView;
public sealed partial class GameLauncher
{
    /// <summary>
    /// Формирует сводную preflight-информацию с путями и статусами перед тестированием.
    /// </summary>
    public GameLaunchPreflight GetPreflightInfo(ModProject? activeMod = null)
    {
        bool isRunning = IsGameRunning(out int? pid);
        var preflight = new GameLaunchPreflight
        {
            GameDirectory = Installation.GameDirectory,
            ExePath = Installation.ExePath,
            BfPath = Installation.BfPath,
            BackupPath = Installation.BackupPath,
            IsExePresent = Installation.ExeExists,
            IsBfPresent = Installation.BfExists,
            IsBackupPresent = Installation.BackupExists,
            IsModInstalled = _isModInstalled,
            IsGameRunning = isRunning,
            RunningPid = pid,
            ActiveModName = _installedModName ?? activeMod?.Name
        };
        if (!preflight.IsExePresent)
            preflight.Errors.Add($"BGE.exe not found: {Installation.ExePath}");
        if (!preflight.IsBfPresent)
            preflight.Errors.Add($"sally_clean.bf not found: {Installation.BfPath}");
        if (isRunning)
            preflight.Warnings.Add($"BGE.exe is already running (PID {pid}). Close the game before installing a mod or restarting.");
        if (activeMod != null && !string.IsNullOrEmpty(activeMod.BaseBfSha256) && preflight.IsBfPresent)
        {
            // Проверка хеша при наличии
            try
            {
                using var archiveStream = File.OpenRead(Installation.BfPath);
                var actualHash = Convert.ToHexStringLower(SHA256.HashData(archiveStream));
                if (!string.Equals(activeMod.BaseBfSha256, actualHash, StringComparison.OrdinalIgnoreCase))
                {
                    preflight.Warnings.Add($"Warning: active BF SHA-256 ({actualHash}) does not match the mod base hash ({activeMod.BaseBfSha256}). " + "Another mod may already be installed.");
                }
            }
            catch (Exception ex)
            {
                preflight.Warnings.Add($"Failed to compute BF checksum: {ex.Message}");
            }
        }

        return preflight;
    }
}
