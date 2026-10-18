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

/// <summary>
/// Результат запуска процесса игры.
/// </summary>
public sealed class GameLaunchResult
{
    public bool IsSuccess => Error == null;
    public int? ProcessId => Process?.Id;
    public Process? Process { get; init; }
    public bool AlreadyRunning { get; init; }
    public string? Error { get; init; }

    public static GameLaunchResult Success(Process process) => new()
    {
        Process = process
    };

    public static GameLaunchResult AlreadyRunningResult(int pid, string error) => new()
    {
        AlreadyRunning = true,
        Error = error
    };

    public static GameLaunchResult Failed(string error) => new()
    {
        Error = error
    };

    public override string ToString() =>
        IsSuccess
            ? $"Game started (PID: {ProcessId})"
            : $"Failed to start game: {Error}";
}

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

/// <summary>
/// Сервис тестирования мода в оригинальной игре Beyond Good &amp; Evil (Агент A6).
/// Выполняет:
///   1. Определение путей и валидацию BGE.exe / sally_clean.bf.
///   2. Безопасное резервное копирование (backup) sally_clean.bf перед установкой мода.
///   3. Сборку нового BF со staging-патчами и установку в тестовую игру.
///   4. Запуск процесса игры с защитой от повторного запуска (PID/Mutex check).
///   5. Гарантированное восстановление оригинального sally_clean.bf из бэкапа.
/// </summary>
public sealed class GameLauncher : IDisposable
{
    public const string ProcessName = "BGE";
    public const string MutexName = "BeyondGoodAndEvilGame";
    public const string DefaultGameDir = GameInstallation.DefaultGameDirectory;
    public const string DefaultCleanBf = GameInstallation.DefaultGameDirectory + @"\sally_clean.bf";
    public const string TargetBfName = "sally.bf";
    public const string GameExeName = "BGE.exe";

    private Process? _activeGameProcess;
    private bool _isModInstalled;
    private string? _installedModName;
    private readonly object _lock = new();

    public GameInstallation Installation { get; private set; }

    public bool IsModInstalled => _isModInstalled;
    public string? InstalledModName => _installedModName;

    public Process? ActiveProcess => _activeGameProcess;

    public event Action<GameInstallation>? OnInstallationChanged;
    public event Action<Process>? OnGameStarted;
    public event Action<int>? OnGameExited;
    public event Action<float>? OnOperationProgress;

    public static string FindGameExecutable(string? candidateDir = null) =>
        GameInstallation.Discover(candidateDir).ExePath;

    public static bool IsGameRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName(ProcessName);
            return processes.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static (bool Success, string Message) LaunchOriginalGame(string? gameExePath = null)
    {
        var launcher = new GameLauncher(string.IsNullOrEmpty(gameExePath) ? null : Path.GetDirectoryName(gameExePath));
        var res = launcher.LaunchGame();
        return (res.IsSuccess, res.Error ?? $"Game started (PID {res.ProcessId})");
    }

    public static (bool Success, string Message) RestoreOriginal(string? gameExePath = null)
    {
        var launcher = new GameLauncher(string.IsNullOrEmpty(gameExePath) ? null : Path.GetDirectoryName(gameExePath));
        var res = launcher.RestoreOriginalBf();
        return (res.IsSuccess, res.Error ?? $"Original archive restored: {res.RestoredPath}");
    }

    public static (bool Success, string Message) DeployModAndLaunch(string moddedBfPath, string? gameExePath = null)
    {
        if (!File.Exists(moddedBfPath))
            return (false, $"Mod file not found: {moddedBfPath}");

        var install = GameInstallation.Discover(string.IsNullOrEmpty(gameExePath) ? null : Path.GetDirectoryName(gameExePath));
        if (IsGameRunning())
            return (false, "Game is running. Close BGE.exe before installing a mod.");

        var launcher = new GameLauncher(install.GameDirectory);
        var backupRes = launcher.EnsureBackupExists();
        if (!backupRes.IsSuccess)
            return (false, $"Failed to create backup: {backupRes.Error}");

        try
        {
            File.Copy(moddedBfPath, install.BfPath, overwrite: true);
            return LaunchOriginalGame(install.ExePath);
        }
        catch (Exception ex)
        {
            return (false, $"Mod deployment failed: {ex.Message}");
        }
    }

    public GameLauncher(string? customGameDir = null)
    {
        Installation = GameInstallation.Discover(customGameDir);
    }

    /// <summary>
    /// Перенастраивает рабочую директорию игры и обновляет пути.
    /// </summary>
    public GameInstallation SetGameDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Installation = GameInstallation.Discover(path);
        OnInstallationChanged?.Invoke(Installation);
        return Installation;
    }

    /// <summary>
    /// Проверяет, запущен ли в данный момент процесс BGE.exe в операционной системе.
    /// Предотвращает попытки двойного запуска и модификации занятых files.
    /// </summary>
    public bool IsGameRunning(out int? runningPid)
    {
        runningPid = null;

        lock (_lock)
        {
            if (_activeGameProcess != null && !_activeGameProcess.HasExited)
            {
                runningPid = _activeGameProcess.Id;
                return true;
            }
        }

        try
        {
            var processes = Process.GetProcessesByName(ProcessName);
            if (processes.Length > 0)
            {
                runningPid = processes[0].Id;
                return true;
            }
        }
        catch
        {
            // Игнорируем ошибки опроса процессов
        }

        // Проверяем глобальный именованный мьютекс игры, если создан
        try
        {
            if (Mutex.TryOpenExisting(MutexName, out var mutex))
            {
                mutex.Dispose();
                return true;
            }
        }
        catch
        {
            // Игнорируем
        }

        return false;
    }

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
                using var s = File.OpenRead(Installation.BfPath);
                var actualHash = Convert.ToHexStringLower(SHA256.HashData(s));
                if (!string.Equals(activeMod.BaseBfSha256, actualHash, StringComparison.OrdinalIgnoreCase))
                {
                    preflight.Warnings.Add($"Warning: active BF SHA-256 ({actualHash}) does not match the mod base hash ({activeMod.BaseBfSha256}). " +
                                          "Another mod may already be installed.");
                }
            }
            catch (Exception ex)
            {
                preflight.Warnings.Add($"Failed to compute BF checksum: {ex.Message}");
            }
        }

        return preflight;
    }

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
                    float p = totalBytes > 0 ? (float)copiedBytes / totalBytes : 0f;
                    progress?.Invoke(p);
                    OnOperationProgress?.Invoke(p);
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
                try { File.Delete(tempBackup); } catch { /* ignore */ }
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
                    float p = totalBytes > 0 ? (float)copiedBytes / totalBytes : 0f;
                    progress?.Invoke(p);
                    OnOperationProgress?.Invoke(p);
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
                try { File.Delete(tempTarget); } catch { /* ignore */ }
            }
        }
    }

    /// <summary>
    /// Собирает новый BF со staging-патчами и устанавливает его в тестовую игру.
    /// Исходный архив baseArchive открывается исключительно на чтение.
    /// Перед установкой автоматически гарантируется создание резервной копии.
    /// </summary>
    public ModInstallResult InstallMod(
        Archive baseArchive,
        WritePlan writePlan,
        byte[] patchedContainerBytes,
        string modName = "Test mod",
        Action<float>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(baseArchive);
        ArgumentNullException.ThrowIfNull(writePlan);
        ArgumentNullException.ThrowIfNull(patchedContainerBytes);

        if (IsGameRunning(out int? pid))
        {
            return ModInstallResult.Failed($"Cannot install mod: BGE.exe is running (PID {pid}). Close the game.");
        }

        if (!Installation.IsValid)
        {
            return ModInstallResult.Failed($"Invalid game installation: {string.Join("; ", Installation.ValidationErrors)}");
        }

        // 1. Гарантируем создание бэкапа оригинала перед любой модификацией
        var backupRes = EnsureBackupExists(p => progress?.Invoke(p * 0.3f));
        if (!backupRes.IsSuccess)
        {
            return ModInstallResult.Failed($"Failed to back up original: {backupRes.Error}");
        }

        // 2. Preflight-проверка плана записи
        var preflight = writePlan.PreflightCheck(patchedContainerBytes);
        if (!preflight.IsSuccess)
        {
            return ModInstallResult.Failed($"Write plan preflight rejected: {string.Join("; ", preflight.Errors)}", preflight);
        }

        // 3. Находим запись контейнера в базовом архиве
        var containerEntry = baseArchive.Entries.FirstOrDefault(e =>
            string.Equals(e.Name, writePlan.ContainerName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.Id, writePlan.ContainerName, StringComparison.OrdinalIgnoreCase));

        if (containerEntry == null)
        {
            return ModInstallResult.Failed($"Container '{writePlan.ContainerName}' not found in base archive {baseArchive.PathName}.", preflight);
        }

        // 4. Сборка нового BF во временный staging-файл
        string stagingBf = Path.Combine(Path.GetTempPath(), $"bge_staging_{Guid.NewGuid():N}.bf");

        try
        {
            SceneWriter.BuildAndSaveArchive(
                baseArchive,
                stagingBf,
                containerEntry.Index,
                patchedContainerBytes,
                p =>
                {
                    float totalProgress = 0.3f + p * 0.6f;
                    progress?.Invoke(totalProgress);
                    OnOperationProgress?.Invoke(totalProgress);
                });

            // 5. Проверка собранного staging архива на валидность BIG 0x22
            var verifyArchive = new Archive(stagingBf);
            if (verifyArchive.Entries.Count != baseArchive.Entries.Count)
            {
                throw new InvalidDataException("Built archive entry count differs from the base archive.");
            }

            // 6. Атомарная установка в игру
            if (IsGameRunning(out pid))
            {
                return ModInstallResult.Failed($"Game was started during the build (PID {pid}). Installation cancelled.");
            }

            File.Move(stagingBf, Installation.BfPath, overwrite: true);

            _isModInstalled = true;
            _installedModName = modName;

            progress?.Invoke(1.0f);
            OnOperationProgress?.Invoke(1.0f);

            return ModInstallResult.Success(
                Installation.BfPath,
                stagingBf,
                writePlan.Patches.Count,
                preflight.BytesToModify,
                preflight);
        }
        catch (Exception ex)
        {
            return ModInstallResult.Failed($"Failed to build and install mod: {ex.Message}", preflight);
        }
        finally
        {
            if (File.Exists(stagingBf))
            {
                try { File.Delete(stagingBf); } catch { /* ignore */ }
            }
        }
    }

    /// <summary>
    /// Запускает оригинальный процесс BGE.exe с проверкой на повторный запуск и отслеживанием жизненного цикла.
    /// </summary>
    public GameLaunchResult LaunchGame(string? customArguments = null)
    {
        if (IsGameRunning(out int? runningPid))
        {
            return GameLaunchResult.AlreadyRunningResult(
                runningPid!.Value,
                $"Beyond Good & Evil is already running (PID {runningPid}). Duplicate launch prevented.");
        }

        if (!Installation.ExeExists)
        {
            return GameLaunchResult.Failed($"Game executable not found: {Installation.ExePath}");
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Installation.ExePath,
                WorkingDirectory = Installation.GameDirectory,
                Arguments = customArguments ?? "/B sally_clean.bf",
                UseShellExecute = false
            };

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            process.Exited += (sender, args) =>
            {
                int exitCode = -1;
                try { exitCode = process.ExitCode; } catch { /* ignore */ }

                lock (_lock)
                {
                    if (ReferenceEquals(_activeGameProcess, process))
                    {
                        _activeGameProcess = null;
                    }
                }

                OnGameExited?.Invoke(exitCode);
            };

            if (!process.Start())
            {
                return GameLaunchResult.Failed("Failed to start BGE.exe.");
            }

            lock (_lock)
            {
                _activeGameProcess = process;
            }

            OnGameStarted?.Invoke(process);
            return GameLaunchResult.Success(process);
        }
        catch (Exception ex)
        {
            return GameLaunchResult.Failed($"Game launch exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Завершает запущенный процесс игры (если он был запущен или завис).
    /// </summary>
    public bool TerminateGame()
    {
        lock (_lock)
        {
            if (_activeGameProcess != null && !_activeGameProcess.HasExited)
            {
                try
                {
                    _activeGameProcess.Kill(entireProcessTree: true);
                    _activeGameProcess.WaitForExit(3000);
                    _activeGameProcess = null;
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        try
        {
            var procs = Process.GetProcessesByName(ProcessName);
            foreach (var p in procs)
            {
                try { p.Kill(); } catch { /* ignore */ }
            }
            return procs.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _activeGameProcess?.Dispose();
            _activeGameProcess = null;
        }
    }
}
