var options = CaptureOptions.Parse(args);
if (options == null)
{
    Console.Error.WriteLine("Usage: CrashCapture <game.exe> <output-directory> OR --attach <pid> <output-directory>");
    return 2;
}

Directory.CreateDirectory(options.OutputDirectory);
var log = new CaptureLog(options.OutputDirectory);
try
{
    new CaptureSession(options, log).Run();
    return 0;
}
catch (Exception error)
{
    log.Write(error.ToString());
    return 1;
}
