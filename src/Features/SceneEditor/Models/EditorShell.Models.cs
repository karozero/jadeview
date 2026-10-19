namespace JadeView;
public sealed partial class EditorShell
{
    private sealed record ArchiveSceneItem(Entry Entry, string Label, string[] WorldNames, string? Diagnostic);
    private sealed class FolderNode(string name, string path)
    {
        public string Name = name, Path = path;
        public int Count;
        public SortedDictionary<string, FolderNode> Children = new(StringComparer.OrdinalIgnoreCase);
    }
}
