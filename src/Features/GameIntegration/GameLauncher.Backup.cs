using System.Diagnostics;
using System.Security.Cryptography;

namespace JadeView;
public sealed partial class GameLauncher
{
    /// <summary>
    /// Гарантирует наличие безопасной резервной копии оригинального игрового архива.
    /// Если бэкап уже существует, он НИКОГДА не перезаписывается, защищая чистый оригинал от затирания модом.
    /// </summary>
    public BackupResult EnsureBackupExists(Action<float>? progress = null)
    {
        if (!Installation.BfExists)
        {
            return BackupResult.Failed($"Source archive not found: {Installation.BfPath}");
        }

        string backupPath = Installation.BackupPath;
        // Если бэкап уже есть на диске и его размер больше 0, не трогаем его
        if (File.Exists(backupPath))
        {
            var existingInfo = new FileInfo(backupPath);
            if (existingInfo.Length > 0)
            {
                return BackupResult.Success(backupPath, existingInfo.Length, null, wasPresent: true);
            }
        }

        // Создаем резервную копию
        string? backupDir = Path.GetDirectoryName(backupPath);
        if (!string.IsNullOrEmpty(backupDir) && !Directory.Exists(backupDir))
        {
            Directory.CreateDirectory(backupDir);
        }

        string tempBackup = backupPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            long totalBytes = new FileInfo(Installation.BfPath).Length;
            long copiedBytes = 0;
            using (var src = new FileStream(Installation.BfPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4 * 1024 * 1024))
            using (var dst = new FileStream(tempBackup, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4 * 1024 * 1024))
            {
                byte[] buffer = new byte[4 * 1024 * 1024];
                int read;
                while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
                {
                    dst.Write(buffer, 0, read);
                    copiedBytes += read;
                    float fraction = totalBytes > 0 ? (float)copiedBytes / totalBytes : 0f;
                    progress?.Invoke(fraction);
                    OnOperationProgress?.Invoke(fraction);
                }

                dst.Flush(true);
            }

            File.Move(tempBackup, backupPath, overwrite: true);
            var finalInfo = new FileInfo(backupPath);
            return BackupResult.Success(backupPath, finalInfo.Length, null, wasPresent: false);
        }
        catch (Exception ex)
        {
            return BackupResult.Failed($"Failed to create backup: {ex.Message}");
        }
        finally
        {
            if (File.Exists(tempBackup))
            {
                try
                {
                    File.Delete(tempBackup);
                }
                catch
                { /* ignore */
                }
            }
        }
    }

    /// <summary>
    /// Гарантированное восстановление оригинального архива из резервной копии.
    /// Перед восстановлением проверяет, что процесс игры не запущен.
    /// </summary>
    public RestoreResult RestoreOriginalBf(Action<float>? progress = null)
    {
        if (IsGameRunning(out int? pid))
        {
            return RestoreResult.Failed($"Cannot restore archive: BGE.exe is running (PID {pid}). Close the game before restoring.");
        }

        string backupPath = Installation.BackupPath;
        if (!File.Exists(backupPath))
        {
            // Проверяем альтернативный путь бэкапа в корне
            string altBackup = Path.Combine(Installation.GameDirectory, GameInstallation.BackupFileName);
            if (File.Exists(altBackup))
            {
                backupPath = altBackup;
            }
            else
            {
                return RestoreResult.Failed($"Backup file not found at: {Installation.BackupPath}");
            }
        }

        string targetBf = Installation.BfPath;
        string tempTarget = targetBf + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            long totalBytes = new FileInfo(backupPath).Length;
            long copiedBytes = 0;
            using (var src = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4 * 1024 * 1024))
            using (var dst = new FileStream(tempTarget, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4 * 1024 * 1024))
            {
                byte[] buffer = new byte[4 * 1024 * 1024];
                int read;
                while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
                {
                    dst.Write(buffer, 0, read);
                    copiedBytes += read;
                    float fraction = totalBytes > 0 ? (float)copiedBytes / totalBytes : 0f;
                    progress?.Invoke(fraction);
                    OnOperationProgress?.Invoke(fraction);
                }

                dst.Flush(true);
            }

            File.Move(tempTarget, targetBf, overwrite: true);
            _isModInstalled = false;
            _installedModName = null;
            var finalInfo = new FileInfo(targetBf);
            return RestoreResult.Success(targetBf, finalInfo.Length);
        }
        catch (Exception ex)
        {
            return RestoreResult.Failed($"Failed to restore original archive: {ex.Message}");
        }
        finally
        {
            if (File.Exists(tempTarget))
            {
                try
                {
                    File.Delete(tempTarget);
                }
                catch
                { /* ignore */
                }
            }
        }
    }
}
