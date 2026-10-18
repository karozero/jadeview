using System.Buffers.Binary;
using System.Text;

namespace JadeView;

public sealed record Entry(int Index, uint Key, uint Offset, int StoredSize, string Name, string Folder)
{
    public bool Compressed => (Key >> 28) == 15 && (Key >> 24) != 0xF4 && (Key >> 20) != 0xFF4;
    public string Id => Key.ToString("X8");
    public string ExportName => Id + "_" + SafeName(Name);
    public static string SafeName(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' ? '_' : c)).TrimEnd('.', ' ') is { Length: > 0 } s ? s : "resource.bin";
}

