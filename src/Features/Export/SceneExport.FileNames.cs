namespace JadeView;

public sealed partial class SceneExporter
{
    private const int MaximumFileNameLength = 160;
    private static readonly HashSet<string> ReservedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string value = new(name.Select(character => char.IsControl(character) || invalid.Contains(character) ? '_' : character).ToArray());
        value = value.Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(value)) value = "export";
        string stem = value.Split('.')[0];
        if (ReservedFileNames.Contains(stem)) value = "_" + value;
        return value.Length > MaximumFileNameLength ? value[..MaximumFileNameLength].TrimEnd('.') : value;
    }

}
