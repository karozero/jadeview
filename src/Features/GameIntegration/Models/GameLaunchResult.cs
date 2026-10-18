using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace JadeView;

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

