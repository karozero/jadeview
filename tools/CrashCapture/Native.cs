using System.Runtime.InteropServices;
using System.Text;

static class Native
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct StartupInfo
    {
        public int Size;
        public string? Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
        public ushort Show, ReservedSize;
        public nint Reserved2, Input, Output, Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInfo
    {
        public nint Process, Thread;
        public uint Id, ThreadId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 176)]
    internal struct DebugEvent
    {
        [FieldOffset(0)]
        public uint Code;
        [FieldOffset(4)]
        public uint ProcessId;
        [FieldOffset(8)]
        public uint ThreadId;
        [FieldOffset(16)]
        public uint ExceptionCode;
        [FieldOffset(32)]
        public ulong ExceptionAddress;
        [FieldOffset(40)]
        public uint ParameterCount;
        [FieldOffset(48)]
        public ulong Arg0;
        [FieldOffset(56)]
        public ulong Arg1;
        [FieldOffset(168)]
        public uint FirstChance;
        [FieldOffset(16)]
        public nint Handle1;
        [FieldOffset(24)]
        public nint Handle2;
        [FieldOffset(32)]
        public nint Handle3;
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool CreateProcess(string application, StringBuilder command, nint pa, nint ta, bool inherit, uint flags, nint environment, string? cwd, ref StartupInfo startup, out ProcessInfo process);
    [DllImport("kernel32", SetLastError = true)]
    internal static extern bool WaitForDebugEvent(out DebugEvent ev, uint milliseconds);
    [DllImport("kernel32")]
    internal static extern bool ContinueDebugEvent(uint process, uint thread, uint status);
    [DllImport("kernel32")]
    internal static extern bool DebugSetProcessKillOnExit(bool kill);
    [DllImport("kernel32")]
    internal static extern bool DebugActiveProcessStop(uint process);
    [DllImport("kernel32", SetLastError = true)]
    internal static extern bool DebugActiveProcess(uint process);
    [DllImport("kernel32", SetLastError = true)]
    internal static extern nint OpenProcess(uint access, bool inherit, uint process);
    [DllImport("kernel32")]
    internal static extern bool CloseHandle(nint handle);
    [DllImport("dbghelp", SetLastError = true)]
    internal static extern bool MiniDumpWriteDump(nint process, uint id, nint file, uint type, nint exception, nint userStreams, nint callback);
}
