using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

internal sealed class CaptureSession(CaptureOptions options, CaptureLog log)
{
    private const uint ExceptionEvent = 1;
    private const uint CreateThreadEvent = 2;
    private const uint CreateProcessEvent = 3;
    private const uint ExitProcessEvent = 5;
    private const uint LoadDllEvent = 6;
    private const uint ContinueExecution = 0x10002;
    private const uint ExceptionNotHandled = 0x80010001;
    private const uint AccessViolation = 0xC0000005;
    private const uint FullDumpFlags = 0x2 | 0x4 | 0x20 | 0x800 | 0x1000;
    private Native.ProcessInfo process;
    internal void Run()
    {
        log.Write("Initializing crash capture.");
        Native.DebugSetProcessKillOnExit(false);
        string game = options.Attach ? AttachToProcess() : StartProcess();
        Native.DebugSetProcessKillOnExit(false);
        if (process.Thread != 0)
            Native.CloseHandle(process.Thread);
        string operation = options.Attach ? "Attached to" : "Started";
        log.Write($"{operation} {game}; PID={process.Id}. Waiting for an unhandled exception. No game files are modified.");
        try
        {
            ObserveEvents();
        }
        finally
        {
            Native.DebugActiveProcessStop(process.Id);
            Native.CloseHandle(process.Process);
        }
    }

    private string AttachToProcess()
    {
        uint processId = uint.Parse(options.ProcessId!);
        using var target = Process.GetProcessById(checked((int)processId));
        string game = target.MainModule?.FileName ?? throw new InvalidOperationException("Cannot identify target executable.");
        bool isGameExecutable = Path.GetFileName(game).Equals("BGE.exe", StringComparison.OrdinalIgnoreCase);
        if (!isGameExecutable)
            throw new InvalidOperationException("Attach mode only accepts BGE.exe.");
        nint handle = Native.OpenProcess(0x1F0FFF, false, processId);
        if (handle == 0)
            throw new Win32Exception();
        if (!Native.DebugActiveProcess(processId))
        {
            Native.CloseHandle(handle);
            throw new Win32Exception();
        }

        process = new Native.ProcessInfo
        {
            Process = handle,
            Id = processId
        };
        return game;
    }

    private string StartProcess()
    {
        var startup = new Native.StartupInfo
        {
            Size = Marshal.SizeOf<Native.StartupInfo>()
        };
        var command = new StringBuilder().Append('"').Append(options.GameExecutable).Append('"');
        bool started = Native.CreateProcess(options.GameExecutable, command, 0, 0, false, 2, 0, Path.GetDirectoryName(options.GameExecutable), ref startup, out process);
        if (!started)
            throw new Win32Exception();
        return options.GameExecutable;
    }

    private void ObserveEvents()
    {
        while (true)
        {
            if (File.Exists(Path.Combine(options.OutputDirectory, "detach.request")))
            {
                log.Write("Detach requested.");
                return;
            }

            if (!Native.WaitForDebugEvent(out var debugEvent, 1000))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 121)
                    continue;
                throw new Win32Exception(error);
            }

            bool isBreakpoint = debugEvent.ExceptionCode is 0x80000003 or 0x4000001F;
            bool shouldForwardException = debugEvent.Code == ExceptionEvent && !isBreakpoint;
            uint status = shouldForwardException ? ExceptionNotHandled : ContinueExecution;
            try
            {
                HandleEvent(debugEvent);
            }
            finally
            {
                Native.ContinueDebugEvent(debugEvent.ProcessId, debugEvent.ThreadId, status);
            }

            if (debugEvent.Code == ExitProcessEvent)
                return;
        }
    }

    private void HandleEvent(Native.DebugEvent debugEvent)
    {
        switch (debugEvent.Code)
        {
            case ExceptionEvent:
                HandleException(debugEvent);
                break;
            case CreateProcessEvent:
                if (debugEvent.Handle1 != 0)
                    Native.CloseHandle(debugEvent.Handle1);
                if (debugEvent.Handle2 != 0 && debugEvent.Handle2 != process.Process)
                    Native.CloseHandle(debugEvent.Handle2);
                if (debugEvent.Handle3 != 0)
                    Native.CloseHandle(debugEvent.Handle3);
                break;
            case CreateThreadEvent:
            case LoadDllEvent:
                if (debugEvent.Handle1 != 0)
                    Native.CloseHandle(debugEvent.Handle1);
                break;
            case ExitProcessEvent:
                log.Write($"Game exited; code=0x{debugEvent.ExceptionCode:X8}");
                break;
        }
    }

    private void HandleException(Native.DebugEvent debugEvent)
    {
        log.Write($"Debug exception: code=0x{debugEvent.ExceptionCode:X8}; address=0x{debugEvent.ExceptionAddress:X}; firstChance={debugEvent.FirstChance}; thread={debugEvent.ThreadId}");
        bool needsDump = debugEvent.FirstChance == 0 || debugEvent.ExceptionCode == AccessViolation;
        // BGE handles the access violation and exits before a second-chance event.
        if (needsDump)
            CaptureDump(debugEvent);
    }

    private void CaptureDump(Native.DebugEvent debugEvent)
    {
        log.Write($"Exception 0x{debugEvent.ExceptionCode:X8} at 0x{debugEvent.ExceptionAddress:X}; firstChance={debugEvent.FirstChance}; PID={debugEvent.ProcessId}; thread={debugEvent.ThreadId}; parameters={debugEvent.ParameterCount}; arg0=0x{debugEvent.Arg0:X}; arg1=0x{debugEvent.Arg1:X}");
        string path = Path.Combine(options.OutputDirectory, $"BGE-{DateTime.UtcNow:yyyyMMdd-HHmmss}.dmp");
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
        bool saved = Native.MiniDumpWriteDump(process.Process, debugEvent.ProcessId, file.SafeFileHandle.DangerousGetHandle(), FullDumpFlags, 0, 0, 0);
        log.Write(saved ? $"Dump saved: {path}" : $"Dump failed: {Marshal.GetLastWin32Error()}");
    }
}
