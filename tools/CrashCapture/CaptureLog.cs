internal sealed class CaptureLog(string outputDirectory)
{
    private readonly string path = Path.Combine(outputDirectory, "capture.log");
    internal void Write(string message)
    {
        string line = $"{DateTimeOffset.Now:O} {message}";
        File.AppendAllText(path, line + Environment.NewLine);
        Console.WriteLine(line);
    }
}
