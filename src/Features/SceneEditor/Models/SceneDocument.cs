using System.Numerics;

namespace JadeView;

public sealed class SceneDocument
{
    public string BaseBfPath { get; set; } = "";
    public string BaseBfSha256 { get; set; } = "";
    public string ContainerName { get; set; } = "";
    public List<WorldData> Worlds { get; } = [];
    public Dictionary<uint, ResourceItem> Resources { get; } = [];
    public List<string> Diagnostics { get; } = [];

    public WorldData? ActiveWorld => Worlds.Count > 0 ? Worlds[0] : null;

    public SceneObject? FindObjectById(string id)
    {
        foreach (var w in Worlds)
        {
            var obj = w.Objects.FirstOrDefault(o => o.Id == id);
            if (obj != null) return obj;
        }
        return null;
    }
}

