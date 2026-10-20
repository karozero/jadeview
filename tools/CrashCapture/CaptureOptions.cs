internal sealed record CaptureOptions(bool Attach, string GameExecutable, string OutputDirectory, string? ProcessId)
{
    internal static CaptureOptions? Parse(string[] arguments)
    {
        bool attach = arguments.Length == 3 && arguments[0] == "--attach";
        bool hasValidArguments = arguments.Length == 2 || attach;
        if (!hasValidArguments)
            return null;
        string executable = attach ? "" : Path.GetFullPath(arguments[0]);
        string output = Path.GetFullPath(arguments[attach ? 2 : 1]);
        if (!attach && !File.Exists(executable))
            throw new FileNotFoundException("Game executable not found", executable);
        return new CaptureOptions(attach, executable, output, attach ? arguments[1] : null);
    }
}
