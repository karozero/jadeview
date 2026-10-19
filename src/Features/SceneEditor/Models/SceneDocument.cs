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
    // Retain keys after deletion: the source stream and undo history still own them.
    internal HashSet<uint> ReservedResourceKeys { get; } = [];

    public uint AllocateObjectKey()
    {
        ReservedResourceKeys.UnionWith(Resources.Keys);
        ReservedResourceKeys.UnionWith(Worlds.SelectMany(w => w.Objects).Select(o => o.GaoKey));
        for (uint key = 0xFEFFFFFF; key > 0xFE000000; key--)
            if (ReservedResourceKeys.Add(key)) return key;
        throw new InvalidOperationException("No free key for a new scene object.");
    }

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

