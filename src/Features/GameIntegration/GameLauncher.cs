using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace JadeView;
/// <summary>
/// Сервис тестирования мода в оригинальной игре Beyond Good &amp; Evil (Агент A6).
/// Выполняет:
///   1. Определение путей и валидацию BGE.exe / sally_clean.bf.
///   2. Безопасное резервное копирование (backup) sally_clean.bf перед установкой мода.
///   3. Сборку нового BF со staging-патчами и установку в тестовую игру.
///   4. Запуск процесса игры с защитой от повторного запуска (PID/Mutex check).
///   5. Гарантированное восстановление оригинального sally_clean.bf из бэкапа.
/// </summary>
public sealed partial class GameLauncher : IDisposable
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
    public static string FindGameExecutable(string? candidateDir = null) => GameInstallation.Discover(candidateDir).ExePath;
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
    /// Запускает оригинальный процесс BGE.exe с проверкой на повторный запуск и отслеживанием жизненного цикла.
    /// </summary>
    public GameLaunchResult LaunchGame(string? customArguments = null)
    {
        if (IsGameRunning(out int? runningPid))
        {
            return GameLaunchResult.AlreadyRunningResult(runningPid!.Value, $"Beyond Good & Evil is already running (PID {runningPid}). Duplicate launch prevented.");
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
            var process = new Process
            {
                StartInfo = psi,
                EnableRaisingEvents = true
            };
            process.Exited += (sender, args) =>
            {
                int exitCode = -1;
                try
                {
                    exitCode = process.ExitCode;
                }
                catch
                { /* ignore */
                }

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
            foreach (var candidateProcess in procs)
            {
                try
                {
                    candidateProcess.Kill();
                }
                catch
                { /* ignore */
                }
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
